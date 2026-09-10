using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ClimateChangeTypologyService"/> (RAL-247): code uniqueness and
/// normalisation, category whitelist, the multi-code guard, and soft delete.
/// </summary>
public sealed class ClimateChangeTypologyServiceTests
{
    private static ClimateChangeTypology Cc(
        int id, string code, string category = "Adaptation", bool active = true) => new()
    {
        Id = id, Code = code, Name = code, Category = category, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (ClimateChangeTypologyService sut, Mock<IClimateChangeTypologyRepository> repo)
        Build(List<ClimateChangeTypology> seed)
    {
        Mock<IClimateChangeTypologyRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()))
            .Callback<ClimateChangeTypology, CancellationToken>((t, _) => seed.Add(t))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new ClimateChangeTypologyService(
            repo.Object, NullLogger<ClimateChangeTypologyService>.Instance, Mock.Of<IAuditService>()), repo);
    }

    private static UpsertClimateChangeTypologyDto Dto(
        string code, string name = "n", string category = "Adaptation") => new(code, name, category);

    [Fact]
    public async Task CreateAsync_WithLowercaseCode_NormalisesToUpper()
    {
        (ClimateChangeTypologyService sut, _) = Build([]);

        ServiceResult<ClimateChangeTypologyDto> result = await sut.CreateAsync(Dto(" a113-08 "));

        Assert.True(result.IsSuccess);
        Assert.Equal("A113-08", result.Value!.Code);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateCodeDifferingOnlyByCase_ReturnsConflict()
    {
        (ClimateChangeTypologyService sut, _) = Build([Cc(1, "A113-08")]);

        ServiceResult<ClimateChangeTypologyDto> result = await sut.CreateAsync(Dto("a113-08"));

        Assert.False(result.IsSuccess);
    }

    [Theory]
    [InlineData("A222-03, A224-05")]
    [InlineData("A123-01; A314-08")]
    public async Task CreateAsync_WithMultiCodeValue_IsRejected(string pasted)
    {
        // Both separators appear in the real FY2027 data. Accepting one of these values would
        // put a multi-code string back into the vocabulary this table exists to replace.
        (ClimateChangeTypologyService sut, Mock<IClimateChangeTypologyRepository> repo) = Build([]);

        ServiceResult<ClimateChangeTypologyDto> result = await sut.CreateAsync(Dto(pasted));

        Assert.False(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithUnknownCategory_ReturnsBadRequest()
    {
        (ClimateChangeTypologyService sut, _) = Build([]);

        ServiceResult<ClimateChangeTypologyDto> result =
            await sut.CreateAsync(Dto("A113-08", category: "Resilience"));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteAsync_DeactivatesButKeepsTheRow()
    {
        // AIP activities reference these codes on an audited document — a hard delete would
        // make a historical activity unreadable.
        List<ClimateChangeTypology> seed = [Cc(1, "A113-08")];
        (ClimateChangeTypologyService sut, Mock<IClimateChangeTypologyRepository> repo) = Build(seed);

        ServiceResult<ClimateChangeTypologyDto> result = await sut.DeleteAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
        Assert.Single(seed);
        repo.Verify(r => r.DeleteAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_ActiveFilter_HidesDeactivatedCodes()
    {
        (ClimateChangeTypologyService sut, _) =
            Build([Cc(1, "A113-08"), Cc(2, "M314-03", "Mitigation", active: false)]);

        IReadOnlyList<ClimateChangeTypologyDto> active =
            await sut.GetAllAsync(null, ActiveFilter.Active);
        IReadOnlyList<ClimateChangeTypologyDto> all =
            await sut.GetAllAsync(null, ActiveFilter.All);

        Assert.Single(active);
        Assert.Equal(2, all.Count);
    }

    /// <summary>Reads a property off an anonymous audit-snapshot object.</summary>
    private static object? Prop(object? snapshot, string name) =>
        snapshot?.GetType().GetProperty(name)?.GetValue(snapshot);

    [Fact]
    public async Task DeleteAsync_OnAnAlreadyInactiveRow_DoesNotLogAFalseTransition()
    {
        // The audit log is read back in Recent Activity, so an entry claiming true -> false on a
        // row that was already inactive is worse than no entry at all (RAL-246).
        Mock<IAuditService> audit = new();
        Mock<IClimateChangeTypologyRepository> repo = new();
        List<ClimateChangeTypology> seed = [Cc(1, "A113-08", active: false)];
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.UpdateAsync(It.IsAny<ClimateChangeTypology>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        ClimateChangeTypologyService sut = new(
            repo.Object, NullLogger<ClimateChangeTypologyService>.Instance, audit.Object);
        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "climate_change_typologies", 1, It.IsAny<string>(),
            It.Is<object?>(o => Equals(Prop(o, "IsActive"), false)),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetCountAsync (RAL-260) ───────────────────────────────────────────────

    [Theory]
    [InlineData(ActiveFilter.Active, true)]
    [InlineData(ActiveFilter.Inactive, false)]
    [InlineData(ActiveFilter.All, null)]
    public async Task GetCountAsync_MapsTheFilterAndCountsInSql(ActiveFilter filter, bool? expected)
    {
        // The count must reach the repository as a filter, not be measured off a materialised
        // list — that is the whole point of the endpoint (RAL-232).
        Mock<IClimateChangeTypologyRepository> repo = new();
        repo.Setup(r => r.CountAsync(It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        ClimateChangeTypologyService sut = new(
            repo.Object, NullLogger<ClimateChangeTypologyService>.Instance, Mock.Of<IAuditService>());

        int count = await sut.GetCountAsync("abc", filter);

        Assert.Equal(7, count);
        repo.Verify(r => r.CountAsync(expected, "abc", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
