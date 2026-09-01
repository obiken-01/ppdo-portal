using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="EsreCodeService"/> (RAL-248): code uniqueness and normalisation,
/// soft delete, and the active filter.
///
/// The eSRE vocabulary is four codes — SS / ES / ID / EN — and the point of the table is that
/// they become selectable instead of typed: one row in the FY2027 file reads "PPDO/PEO", an
/// implementing-office name typed into the eSRE column (AIP_Form_Spec.md §3.1).
///
/// ⚠️ "ID" is both one of the four codes and the name of the PK column. The fixtures below spell
/// the code as a string constant so the two cannot be misread in an assertion.
/// </summary>
public sealed class EsreCodeServiceTests
{
    /// <summary>The eSRE code, not an identifier — see the class remarks.</summary>
    private const string InfrastructureCode = "ID";

    private static EsreCode Code(int id, string code, bool active = true) => new()
    {
        Id = id, Code = code, Name = code, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (EsreCodeService sut, Mock<IRepository<EsreCode>> repo) Build(List<EsreCode> seed)
    {
        Mock<IRepository<EsreCode>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()))
            .Callback<EsreCode, CancellationToken>((e, _) => seed.Add(e))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new EsreCodeService(
            repo.Object, NullLogger<EsreCodeService>.Instance, Mock.Of<IAuditService>()), repo);
    }

    private static UpsertEsreCodeDto Dto(string code, string name = "n") => new(code, name);

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithLowercaseCode_NormalisesToUpper()
    {
        (EsreCodeService sut, _) = Build([]);

        ServiceResult<EsreCodeDto> result = await sut.CreateAsync(Dto(" ss "));

        Assert.True(result.IsSuccess);
        Assert.Equal("SS", result.Value!.Code);
    }

    [Fact]
    public async Task CreateAsync_WithDuplicateCodeDifferingOnlyByCase_ReturnsConflict()
    {
        (EsreCodeService sut, Mock<IRepository<EsreCode>> repo) = Build([Code(1, "SS")]);

        ServiceResult<EsreCodeDto> result = await sut.CreateAsync(Dto("ss"));

        Assert.False(result.IsSuccess);
        repo.Verify(r => r.AddAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithBlankName_ReturnsBadRequest()
    {
        (EsreCodeService sut, _) = Build([]);

        ServiceResult<EsreCodeDto> result = await sut.CreateAsync(new UpsertEsreCodeDto("SS", "  "));

        Assert.False(result.IsSuccess);
    }

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ActiveFilter_HidesDeactivatedCodes()
    {
        (EsreCodeService sut, _) = Build([Code(1, "SS"), Code(2, "EN", active: false)]);

        IReadOnlyList<EsreCodeDto> active = await sut.GetAllAsync(null, ActiveFilter.Active);
        IReadOnlyList<EsreCodeDto> all    = await sut.GetAllAsync(null, ActiveFilter.All);

        Assert.Single(active);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public async Task GetAllAsync_Search_MatchesCodeOrName()
    {
        List<EsreCode> seed = [Code(1, "SS"), Code(2, "ES")];
        seed[1].Name = "Economic";
        (EsreCodeService sut, _) = Build(seed);

        IReadOnlyList<EsreCodeDto> byName = await sut.GetAllAsync("econom", ActiveFilter.All);

        EsreCodeDto only = Assert.Single(byName);
        Assert.Equal("ES", only.Code);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_WithUnknownId_ReturnsNotFound()
    {
        (EsreCodeService sut, _) = Build([Code(1, "SS")]);

        ServiceResult<EsreCodeDto> result = await sut.GetByIdAsync(99);

        Assert.False(result.IsSuccess);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_RenamingOntoAnotherExistingCode_ReturnsConflict()
    {
        (EsreCodeService sut, _) = Build([Code(1, "SS"), Code(2, InfrastructureCode)]);

        ServiceResult<EsreCodeDto> result = await sut.UpdateAsync(2, Dto("SS"));

        Assert.False(result.IsSuccess);
    }

    [Fact]
    public async Task UpdateAsync_KeepingItsOwnCode_Succeeds()
    {
        // The uniqueness check must exclude the row being edited, or renaming nothing fails.
        (EsreCodeService sut, _) = Build([Code(1, "SS")]);

        ServiceResult<EsreCodeDto> result = await sut.UpdateAsync(1, Dto("SS", "Social Services"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Social Services", result.Value!.Name);
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_DeactivatesButKeepsTheRow()
    {
        // AIP activities reference these codes on an audited document — a hard delete would make
        // a historical activity unreadable.
        List<EsreCode> seed = [Code(1, "SS")];
        (EsreCodeService sut, Mock<IRepository<EsreCode>> repo) = Build(seed);

        ServiceResult<EsreCodeDto> result = await sut.DeleteAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsActive);
        Assert.Single(seed);
        repo.Verify(r => r.DeleteAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()), Times.Never);
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
        Mock<IRepository<EsreCode>> repo = new();
        List<EsreCode> seed = [Code(1, "SS", active: false)];
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.UpdateAsync(It.IsAny<EsreCode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        EsreCodeService sut = new(
            repo.Object, NullLogger<EsreCodeService>.Instance, audit.Object);
        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "esre_codes", 1, It.IsAny<string>(),
            It.Is<object?>(o => Equals(Prop(o, "IsActive"), false)),
            It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
