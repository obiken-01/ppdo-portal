using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
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

    // ── CSV landing_page round-trip (RAL-258) ──────────────────────────────────

    private const string OfficeCsvHeader = "office_code,office_name,is_active,office_ref_code,landing_page";

    [Fact]
    public async Task ExportCsvAsync_IncludesLandingPageColumnAndValue()
    {
        Office withLanding = Off(1, "GSO", "General Services Office");
        withLanding.LandingPage = LandingPage.BudgetPlanningDashboard;
        (OfficeService sut, _) = Build([withLanding]);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("landing_page", csv);
        Assert.Contains("BudgetPlanningDashboard", csv);
    }

    [Fact]
    public async Task ExportCsvAsync_NoPreference_WritesBlankLandingPage()
    {
        (OfficeService sut, _) = Build([Off(1, "GSO", "General Services Office")]);

        string csv = await sut.ExportCsvAsync();

        // Trailing empty cell rather than the string "null" — blank means "no preference".
        Assert.Contains("GSO,General Services Office,true,,", csv);
        Assert.DoesNotContain("null", csv);
    }

    [Fact]
    public async Task ImportCsvAsync_NewOfficeWithLandingPage_SetsIt()
    {
        List<Office> seed = [];
        (OfficeService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            OfficeCsvHeader,
            "GSO,General Services Office,true,,BudgetPlanningDashboard");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(LandingPage.BudgetPlanningDashboard, seed.Single().LandingPage);
    }

    [Fact]
    public async Task ImportCsvAsync_LandingPageIsOnlyChange_CountsAsUpdated()
    {
        Office existing = Off(1, "GSO", "General Services Office");
        (OfficeService sut, _) = Build([existing]);

        string csv = string.Join("\r\n",
            OfficeCsvHeader,
            "GSO,General Services Office,true,,Profile");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        // Without landing_page in the change detection this row would count as skipped and the
        // value would be silently dropped.
        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(0, result.Value.Skipped);
        Assert.Equal(LandingPage.Profile, existing.LandingPage);
    }

    [Fact]
    public async Task ImportCsvAsync_BlankLandingPage_ClearsExistingPreference()
    {
        Office existing = Off(1, "GSO", "General Services Office");
        existing.LandingPage = LandingPage.Profile;
        (OfficeService sut, _) = Build([existing]);

        string csv = string.Join("\r\n",
            OfficeCsvHeader,
            "GSO,General Services Office,true,,");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);
        Assert.Null(existing.LandingPage);
    }

    [Fact]
    public async Task ImportCsvAsync_InvalidLandingPage_SkipsRowWithError()
    {
        Office existing = Off(1, "GSO", "General Services Office");
        existing.LandingPage = LandingPage.Profile;
        (OfficeService sut, _) = Build([existing]);

        string csv = string.Join("\r\n",
            OfficeCsvHeader,
            "GSO,General Services Office,true,,Nonsense");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        // A typo must not be read as "no preference" — that would quietly wipe the setting.
        Assert.Equal(1, result.Value!.Skipped);
        Assert.NotEmpty(result.Value.Errors);
        Assert.Equal(LandingPage.Profile, existing.LandingPage);
    }

    [Fact]
    public async Task ImportCsvAsync_LegacyCsvWithoutLandingPageColumn_LeavesValueUntouched()
    {
        Office existing = Off(1, "GSO", "General Services Office");
        existing.LandingPage = LandingPage.BudgetPlanningDashboard;
        (OfficeService sut, _) = Build([existing]);

        // A file exported before RAL-258 has only four columns.
        string csv = string.Join("\r\n",
            "office_code,office_name,is_active,office_ref_code",
            "GSO,General Services Office,true,");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(0, result.Value!.Updated);
        Assert.Equal(LandingPage.BudgetPlanningDashboard, existing.LandingPage);
    }

    [Fact]
    public async Task ExportThenImportCsv_IsAStableRoundTrip()
    {
        Office withLanding = Off(1, "GSO", "General Services Office");
        withLanding.LandingPage = LandingPage.BudgetPlanningDashboard;
        (OfficeService sut, _) = Build([withLanding]);

        string exported = await sut.ExportCsvAsync();
        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(exported);

        // Re-uploading an untouched export must be a no-op, not an update.
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(0, result.Value.Updated);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Equal(LandingPage.BudgetPlanningDashboard, withLanding.LandingPage);
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
