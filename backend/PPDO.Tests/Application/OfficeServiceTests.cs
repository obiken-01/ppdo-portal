using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="OfficeService"/> (RAL-70 + RAL-77): CSV upsert by office_code,
/// key uniqueness, soft delete, the active (dropdown) filter, and audit log calls.
/// </summary>
public sealed class OfficeServiceTests
{
    private static Office Off(int id, string code, string name, bool active = true) => new()
    {
        Id = id, OfficeCode = code, OfficeName = name, IsActive = active,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (OfficeService sut, Mock<IRepository<Office>> repo) Build(
        List<Office> seed, IAuditService? audit = null)
    {
        Mock<IRepository<Office>> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        repo.Setup(r => r.AddAsync(It.IsAny<Office>(), It.IsAny<CancellationToken>()))
            .Callback<Office, CancellationToken>((o, _) => seed.Add(o))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<Office>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new OfficeService(repo.Object, NullLogger<OfficeService>.Instance,
            audit ?? Mock.Of<IAuditService>()), repo);
    }

    private static (OfficeService sut, Mock<IRepository<Office>> repo, Mock<IAuditService> audit)
        BuildWithAudit(List<Office> seed)
    {
        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        (OfficeService sut, Mock<IRepository<Office>> repo) = Build(seed, audit.Object);
        return (sut, repo, audit);
    }

    [Fact]
    public async Task GetAllAsync_ActiveFilter_ExcludesDeactivated()
    {
        List<Office> seed = [Off(1, "PPDO", "Planning", true), Off(2, "OLD", "Closed Office", false)];
        (OfficeService sut, _) = Build(seed);

        IReadOnlyList<OfficeDto> result = await sut.GetAllAsync(search: null, active: ActiveFilter.Active);

        Assert.Single(result);
        Assert.Equal("PPDO", result[0].OfficeCode);
    }

    [Fact]
    public async Task GetAllAsync_Search_MatchesCodeOrName()
    {
        List<Office> seed = [Off(1, "PPDO", "Planning"), Off(2, "PGO", "Governor's Office")];
        (OfficeService sut, _) = Build(seed);

        Assert.Single(await sut.GetAllAsync("govern", ActiveFilter.All));   // by name
        Assert.Single(await sut.GetAllAsync("ppdo", ActiveFilter.All));     // by code
    }

    [Fact]
    public async Task CreateAsync_DuplicateCode_ReturnsConflict()
    {
        (OfficeService sut, _) = Build([Off(1, "PPDO", "Planning")]);
        ServiceResult<OfficeDto> result = await sut.CreateAsync(new UpsertOfficeDto("PPDO", "Dup"));
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        Office target = Off(1, "PPDO", "Planning");
        (OfficeService sut, Mock<IRepository<Office>> repo) = Build([target]);

        ServiceResult<OfficeDto> result = await sut.DeleteAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
        repo.Verify(r => r.DeleteAsync(It.IsAny<Office>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportCsvAsync_UpsertByCode_CountsNewUpdatedSkipped()
    {
        List<Office> seed = [Off(1, "PPDO", "Planning"), Off(2, "PGO", "Old Name")];
        (OfficeService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "office_code,office_name,is_active",
            "PPDO,Planning,true",                 // unchanged → skipped
            "PGO,Governor's Office,true",         // name changed → updated
            "PTO,Treasurer's Office,true");       // new → inserted

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(1, result.Value.Updated);
        Assert.Equal(1, result.Value.Skipped);
    }

    // ── audit logging (RAL-77) ─────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        (OfficeService sut, _, Mock<IAuditService> audit) = BuildWithAudit([]);

        await sut.CreateAsync(new UpsertOfficeDto("PPDO", "Planning Office"));

        audit.Verify(a => a.LogAsync(
            "offices", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CallsAuditLog_CapturingOldAndNewValues()
    {
        List<Office> seed = [Off(1, "PPDO", "Old Name")];
        (OfficeService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.UpdateAsync(1, new UpsertOfficeDto("PPDO", "New Name"));

        audit.Verify(a => a.LogAsync(
            "offices", 1, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CallsAuditLog_WithDeleteAction()
    {
        List<Office> seed = [Off(1, "PPDO", "Planning", active: true)];
        (OfficeService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "offices", 1, AuditAction.Delete,
            It.IsNotNull<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
