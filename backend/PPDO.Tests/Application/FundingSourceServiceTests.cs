using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="FundingSourceService"/> (RAL-70 + RAL-77): CSV upsert by code,
/// key uniqueness, soft delete, and audit log calls.
/// </summary>
public sealed class FundingSourceServiceTests
{
    private static FundingSource Fs(int id, string code, string name, bool active = true) => new()
    {
        Id = id, Code = code, Name = name, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (FundingSourceService sut, Mock<IRepository<FundingSource>> repo) Build(
        List<FundingSource> seed, IAuditService? audit = null)
    {
        Mock<IRepository<FundingSource>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<FundingSource>(), It.IsAny<CancellationToken>()))
            .Callback<FundingSource, CancellationToken>((f, _) => seed.Add(f))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<FundingSource>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new FundingSourceService(repo.Object, NullLogger<FundingSourceService>.Instance,
            audit ?? Mock.Of<IAuditService>()), repo);
    }

    private static (FundingSourceService sut, Mock<IRepository<FundingSource>> repo, Mock<IAuditService> audit)
        BuildWithAudit(List<FundingSource> seed)
    {
        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        (FundingSourceService sut, Mock<IRepository<FundingSource>> repo) = Build(seed, audit.Object);
        return (sut, repo, audit);
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ReturnsConflict()
    {
        (FundingSourceService sut, _) = Build([Fs(1, "GF", "General Fund")]);
        ServiceResult<FundingSourceDto> result =
            await sut.CreateAsync(new UpsertFundingSourceDto("GF", "Duplicate", null));
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_New_ReturnsOk()
    {
        (FundingSourceService sut, _) = Build([Fs(1, "GF", "General Fund")]);
        ServiceResult<FundingSourceDto> result =
            await sut.CreateAsync(new UpsertFundingSourceDto("GAD", "Gender and Development", "desc"));
        Assert.True(result.IsSuccess);
        Assert.Equal("GAD", result.Value!.Code);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        FundingSource target = Fs(1, "GF", "General Fund");
        (FundingSourceService sut, _) = Build([target]);
        ServiceResult<FundingSourceDto> result = await sut.DeleteAsync(1);
        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
    }

    [Fact]
    public async Task ImportCsvAsync_UpsertByCode_CountsNewUpdatedSkipped()
    {
        List<FundingSource> seed = [Fs(1, "GF", "General Fund"), Fs(2, "GAD", "Old GAD Name")];
        (FundingSourceService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "code,name,description,is_active",
            "GF,General Fund,,true",                          // unchanged → skipped
            "GAD,Gender and Development,Updated desc,true",   // changed → updated
            "SEF,Special Education Fund,,true");              // new → inserted

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(1, result.Value.Updated);
        Assert.Equal(1, result.Value.Skipped);
    }

    // ── audit logging (RAL-77) ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        (FundingSourceService sut, _, Mock<IAuditService> audit) = BuildWithAudit([]);

        await sut.CreateAsync(new UpsertFundingSourceDto("GF", "General Fund", null));

        audit.Verify(a => a.LogAsync(
            "funding_sources", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CallsAuditLog_CapturingOldAndNewValues()
    {
        List<FundingSource> seed = [Fs(1, "GF", "Old Name")];
        (FundingSourceService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.UpdateAsync(1, new UpsertFundingSourceDto("GF", "New Name", null));

        audit.Verify(a => a.LogAsync(
            "funding_sources", 1, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CallsAuditLog_WithDeleteAction()
    {
        List<FundingSource> seed = [Fs(1, "GF", "General Fund", active: true)];
        (FundingSourceService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "funding_sources", 1, AuditAction.Delete,
            It.IsNotNull<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
