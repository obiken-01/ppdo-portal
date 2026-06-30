using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="DivisionService"/> (RAL-98): CRUD, CSV upsert by name within
/// office_code, flag round-trips, soft delete, and audit log calls.
/// </summary>
public sealed class DivisionServiceTests
{
    private static Office Office1 => new() { Id = 1, OfficeCode = "PPDO", OfficeName = "Planning Office", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
    private static Office Office2 => new() { Id = 2, OfficeCode = "PGO", OfficeName = "Governor's Office", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };

    private static Division Div(int id, int officeId, string name, string? code = null, bool active = true) => new()
    {
        Id = id, OfficeId = officeId, Name = name, Code = code, IsActive = active,
        CanAccessBudgetPlanning = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static (DivisionService sut, Mock<IRepository<Division>> divRepo) Build(
        List<Division> divSeed, List<Office> officeSeed, IAuditService? audit = null)
    {
        Mock<IRepository<Division>> divRepo = new();
        divRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(divSeed);
        divRepo.Setup(r => r.AddAsync(It.IsAny<Division>(), It.IsAny<CancellationToken>()))
            .Callback<Division, CancellationToken>((d, _) => divSeed.Add(d))
            .Returns(Task.CompletedTask);
        divRepo.Setup(r => r.UpdateAsync(It.IsAny<Division>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        divRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        Mock<IRepository<Office>> officeRepo = new();
        officeRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(officeSeed);

        DivisionService sut = new(
            divRepo.Object,
            officeRepo.Object,
            NullLogger<DivisionService>.Instance,
            audit ?? Mock.Of<IAuditService>());
        return (sut, divRepo);
    }

    private static (DivisionService sut, Mock<IRepository<Division>> divRepo, Mock<IAuditService> audit)
        BuildWithAudit(List<Division> divSeed, List<Office> officeSeed)
    {
        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        (DivisionService sut, Mock<IRepository<Division>> divRepo) = Build(divSeed, officeSeed, audit.Object);
        return (sut, divRepo, audit);
    }

    // ── GetAllAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ActiveFilter_ExcludesInactive()
    {
        List<Division> seed = [Div(1, 1, "Admin", active: true), Div(2, 1, "ICT", active: false)];
        (DivisionService sut, _) = Build(seed, [Office1]);

        IReadOnlyList<DivisionDto> result = await sut.GetAllAsync(activeOnly: true);

        Assert.Single(result);
        Assert.Equal("Admin", result[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_OfficeFilter_ReturnsDivisionsForOfficeOnly()
    {
        List<Division> seed = [Div(1, 1, "Admin"), Div(2, 2, "Governor Staff")];
        (DivisionService sut, _) = Build(seed, [Office1, Office2]);

        IReadOnlyList<DivisionDto> result = await sut.GetAllAsync(officeId: 1);

        Assert.Single(result);
        Assert.Equal("Admin", result[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_IncludesOfficeName()
    {
        List<Division> seed = [Div(1, 1, "Admin")];
        (DivisionService sut, _) = Build(seed, [Office1]);

        IReadOnlyList<DivisionDto> result = await sut.GetAllAsync();

        Assert.Equal("Planning Office", result[0].OfficeName);
    }

    // ── GetByIdAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNotFound()
    {
        (DivisionService sut, _) = Build([], []);
        ServiceResult<DivisionDto> result = await sut.GetByIdAsync(99);
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── CreateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_ValidDto_ReturnsDto()
    {
        (DivisionService sut, _) = Build([], [Office1]);
        UpsertDivisionDto dto = new(1, "ADMIN", "Administrative Division",
            true, true, false, false, false, false, false, false);

        ServiceResult<DivisionDto> result = await sut.CreateAsync(dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Administrative Division", result.Value!.Name);
        Assert.Equal("ADMIN", result.Value.Code);
        Assert.True(result.Value.CanAccessBudgetPlanning);
    }

    [Fact]
    public async Task CreateAsync_DuplicateNameWithinOffice_ReturnsConflict()
    {
        List<Division> seed = [Div(1, 1, "Admin")];
        (DivisionService sut, _) = Build(seed, [Office1]);

        ServiceResult<DivisionDto> result = await sut.CreateAsync(
            new UpsertDivisionDto(1, null, "Admin", true, false, false, false, false, false, false, false));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_SameNameDifferentOffice_Succeeds()
    {
        List<Division> seed = [Div(1, 1, "Admin")];
        (DivisionService sut, _) = Build(seed, [Office1, Office2]);

        ServiceResult<DivisionDto> result = await sut.CreateAsync(
            new UpsertDivisionDto(2, null, "Admin", true, false, false, false, false, false, false, false));

        Assert.True(result.IsSuccess);
    }

    // ── UpdateAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_FlagsRoundTrip_PersistsAllFlags()
    {
        List<Division> seed = [Div(1, 1, "Admin")];
        (DivisionService sut, _) = Build(seed, [Office1]);

        UpsertDivisionDto dto = new(1, "ADMIN", "Admin", true,
            CanAccessBudgetPlanning: true,
            CanAccessInventory: true,
            CanAccessReports: true,
            CanManageConfig: false,
            CanUploadAip: false,
            CanManageUsers: true,
            CanManageResourceLinks: true);

        ServiceResult<DivisionDto> result = await sut.UpdateAsync(1, dto);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.CanAccessInventory);
        Assert.True(result.Value.CanAccessReports);
        Assert.True(result.Value.CanManageUsers);
        Assert.True(result.Value.CanManageResourceLinks);
        Assert.False(result.Value.CanManageConfig);
        Assert.False(result.Value.CanUploadAip);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        (DivisionService sut, _) = Build([], [Office1]);
        ServiceResult<DivisionDto> result = await sut.UpdateAsync(99,
            new UpsertDivisionDto(1, null, "X", true, false, false, false, false, false, false, false));
        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── DeleteAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        Division target = Div(1, 1, "Admin");
        (DivisionService sut, Mock<IRepository<Division>> divRepo) = Build([target], [Office1]);

        ServiceResult<DivisionDto> result = await sut.DeleteAsync(1);

        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
        divRepo.Verify(r => r.DeleteAsync(It.IsAny<Division>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CSV upsert ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ImportCsvAsync_UpsertByNameWithinOffice_CountsNewUpdatedSkipped()
    {
        List<Division> seed = [Div(1, 1, "Administrative Division"), Div(2, 1, "ICT Division")];
        (DivisionService sut, _) = Build(seed, [Office1]);

        string csv = string.Join("\r\n",
            "office_code,code,name,is_active,can_access_budget_planning,can_access_inventory,can_access_reports,can_manage_config,can_upload_aip,can_manage_users,can_manage_resource_links",
            "PPDO,,Administrative Division,true,true,false,false,false,false,false,false",   // unchanged → skipped
            "PPDO,ICT,ICT Division,true,true,true,false,false,false,false,false",            // code changed → updated
            "PPDO,SECTORAL,Sectoral Planning Division,true,true,false,false,false,false,false,false");  // new → inserted

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv, [Office1]);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(1, result.Value.Updated);
        Assert.Equal(1, result.Value.Skipped);
    }

    [Fact]
    public async Task ImportCsvAsync_UnknownOfficeCode_SkipsRow()
    {
        (DivisionService sut, _) = Build([], [Office1]);

        string csv = string.Join("\r\n",
            "office_code,code,name,is_active,can_access_budget_planning,can_access_inventory,can_access_reports,can_manage_config,can_upload_aip,can_manage_users,can_manage_resource_links",
            "UNKNOWN,,Admin,true,true,false,false,false,false,false,false");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv, [Office1]);

        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.NotEmpty(result.Value.Errors);
    }

    [Fact]
    public async Task ImportCsvAsync_DuplicateNameSameOfficeInCSV_SecondRowSkipped()
    {
        (DivisionService sut, _) = Build([], [Office1]);

        string csv = string.Join("\r\n",
            "office_code,code,name,is_active,can_access_budget_planning,can_access_inventory,can_access_reports,can_manage_config,can_upload_aip,can_manage_users,can_manage_resource_links",
            "PPDO,,Admin,true,true,false,false,false,false,false,false",
            "PPDO,,Admin,true,false,false,false,false,false,false,false");  // duplicate

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv, [Office1]);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(0, result.Value.Updated);
        Assert.Equal(1, result.Value.Skipped);
    }

    // ── CSV export ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportCsvAsync_ReturnsAllFlagColumns()
    {
        List<Division> seed = [Div(1, 1, "Admin", "ADMIN")];
        (DivisionService sut, _) = Build(seed, [Office1]);

        string csv = await sut.ExportCsvAsync([Office1]);

        Assert.Contains("office_code", csv);
        Assert.Contains("can_access_budget_planning", csv);
        Assert.Contains("can_manage_resource_links", csv);
        Assert.Contains("PPDO", csv);
        Assert.Contains("ADMIN", csv);
    }

    // ── Audit logging ──────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        (DivisionService sut, _, Mock<IAuditService> audit) = BuildWithAudit([], [Office1]);

        await sut.CreateAsync(new UpsertDivisionDto(1, null, "Admin", true, false, false, false, false, false, false, false));

        audit.Verify(a => a.LogAsync(
            "divisions", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CallsAuditLog_CapturingOldAndNewValues()
    {
        List<Division> seed = [Div(1, 1, "Admin")];
        (DivisionService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed, [Office1]);

        await sut.UpdateAsync(1, new UpsertDivisionDto(1, "ADM", "Admin", true, true, false, false, false, false, false, false));

        audit.Verify(a => a.LogAsync(
            "divisions", 1, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CallsAuditLog_WithDeleteAction()
    {
        List<Division> seed = [Div(1, 1, "Admin", active: true)];
        (DivisionService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed, [Office1]);

        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "divisions", 1, AuditAction.Delete,
            It.IsNotNull<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
