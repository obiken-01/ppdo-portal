using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// The consolidated AIP sheet (V18-55 / PPDO-73, <c>AIP_Review_Spec.md</c> §6.3a).
///
/// <para>
/// ⚠️ <b>The fixture holds offices in every state on purpose.</b> A read that forgot to filter to
/// "with PPDO" would pass against a fixture of submitted offices alone; the sent-back and drafting
/// offices here are what make the filter visible.
/// </para>
/// </summary>
public sealed class AipConsolidatedServiceTests
{
    private const int FiscalYear = 2028;
    private const int RecordId   = 900;

    private const int PgoGroup      = 101; // office 7, GENERAL, with PPDO
    private const int PpdoGroup     = 102; // office 8, GENERAL, accepted
    private const int ReturnedGroup = 103; // office 9, GENERAL, sent back
    private const int DraftGroup    = 104; // office 10, GENERAL, drafting
    private const int PgoSocial     = 105; // office 7 again, SOCIAL, with PPDO
    private const int EconomicDraft = 106; // office 11, ECONOMIC, drafting

    private readonly Mock<IAipRepository>            _aipRepo     = new();
    private readonly Mock<IAipExpenditureRepository> _expRepo     = new();
    private readonly Mock<IPermissionService>        _permissions = new();

    private IReadOnlyList<int>? _programsAskedFor;
    private bool _recordExists = true;

    private readonly List<AipOffice> _groups =
    [
        Group(PgoGroup, 7, "1000-000-1-01-001", "OFFICE OF THE PROVINCIAL GOVERNOR", AipSector.General, AipWorkflowStatus.SubmittedToPpdo),
        Group(PpdoGroup, 8, "1000-000-1-01-010", "PROVINCIAL PLANNING AND DEVELOPMENT OFFICE", AipSector.General, AipWorkflowStatus.Consolidated),
        Group(ReturnedGroup, 9, "1000-000-1-01-002", "OFFICE OF THE VICE GOVERNOR", AipSector.General, AipWorkflowStatus.ReturnedByPpdo),
        Group(DraftGroup, 10, "1000-000-1-01-003", "SANGGUNIANG PANLALAWIGAN", AipSector.General, AipWorkflowStatus.Draft),
        Group(PgoSocial, 7, "3000-000-1-01-001", "OFFICE OF THE PROVINCIAL GOVERNOR - AKAP-HUB", AipSector.Social, AipWorkflowStatus.SubmittedToPpdo),
        Group(EconomicDraft, 11, "8000-000-1-01-020", "PROVINCIAL AGRICULTURE OFFICE", AipSector.Economic, AipWorkflowStatus.Draft),
    ];

    private readonly List<AipProgram> _programs =
    [
        new() { Id = 201, OfficeId = PgoGroup, RefCode = "1000-000-1-01-001-001", Name = "EXECUTIVE GOVERNANCE PROGRAM" },
        new() { Id = 202, OfficeId = PpdoGroup, RefCode = "1000-000-1-01-010-001", Name = "PLANNING PROGRAM" },
        new() { Id = 203, OfficeId = ReturnedGroup, RefCode = "1000-000-1-01-002-001", Name = "LEGISLATIVE SUPPORT" },
        // Seeded from the LDIP but never worked on — no project, no activity. Must not print.
        new() { Id = 204, OfficeId = PgoGroup, RefCode = "1000-000-1-01-001-009", Name = "EMPTY PROGRAM" },
        // Only a synthetic project, and nothing in it. Prints no row beneath, so must not print either.
        new() { Id = 205, OfficeId = PpdoGroup, RefCode = "1000-000-1-01-010-002", Name = "HOLLOW PROGRAM" },
    ];

    private readonly List<AipProject> _projects =
    [
        new() { Id = 301, ProgramId = 201, RefCode = "1000-000-1-01-001-001-001", Name = "General supervision" },
        // A line recorded directly on its program row (RAL-108) — no row of its own on the form.
        new() { Id = 302, ProgramId = 202, RefCode = "1000-000-1-01-010-001-001", Name = "(synthetic)", IsSynthetic = true },
        new() { Id = 303, ProgramId = 203, RefCode = "1000-000-1-01-002-001-001", Name = "Session support" },
        new() { Id = 305, ProgramId = 205, RefCode = "1000-000-1-01-010-002-001", Name = "(synthetic)", IsSynthetic = true },
    ];

    private readonly List<AipActivity> _activities =
    [
        // Printed: PS 2,000 · MOOE 1,301,000 · Total 1,303,000
        new() { Id = 401, ProjectId = 301, RefCode = "1000-000-1-01-001-001-001-001", Name = "Plantilla positions", EsreCode = "ID", Ps = 1_200m, Mooe = 1_000_400m },
        // Printed: MOOE 2,000 (1,560 rounded up) · Total 2,000
        new() { Id = 402, ProjectId = 301, RefCode = "1000-000-1-01-001-001-001-002", Name = "Admin support", Mooe = 1_200m, FundingSourceSnapshot = "GF" },
        // Printed: CO 2,000 (1,300 rounded up) · CC adaptation 2,000 · Total 2,000
        new() { Id = 403, ProjectId = 302, RefCode = "1000-000-1-01-010-001-001-001", Name = "GIS workstations", Co = 1_000m, CcAdaptation = 1_200m },
        new() { Id = 404, ProjectId = 303, RefCode = "1000-000-1-01-002-001-001-001", Name = "Must never print", Mooe = 5_000m },
    ];

    private static AipOffice Group(int id, int officeId, string refCode, string name, string sector, string status)
        => new()
        {
            Id = id, AipRecordId = RecordId, OfficeId = officeId, RefCode = refCode,
            Name = name, Sector = sector, WorkflowStatus = status,
        };

    private AipConsolidatedService Build()
    {
        _aipRepo.Setup(r => r.GetLatestByFiscalYearAsync(FiscalYear, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _recordExists
                ? new AipRecord
                {
                    Id = RecordId, FiscalYear = FiscalYear, EntrySource = "Manual",
                    Status = PlanningStatus.Draft, UploadedById = Guid.NewGuid(), UploadedAt = DateTime.UtcNow,
                }
                : null);
        _aipRepo.Setup(r => r.GetOfficesByAipIdAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _groups);

        // ⚠️ Each stub answers only for the ids it is asked about, so a read that expanded the wrong
        // offices would print their rows rather than quietly passing.
        _aipRepo.Setup(r => r.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
            {
                _programsAskedFor = ids;
                return _programs.Where(p => ids.Contains(p.OfficeId)).ToList();
            });
        _aipRepo.Setup(r => r.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                _projects.Where(j => ids.Contains(j.ProgramId)).ToList());
        _aipRepo.Setup(r => r.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                _activities.Where(a => ids.Contains(a.ProjectId)).ToList());

        _expRepo.Setup(r => r.GetFundCodesByAipRecordAsync(RecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new AipActivityFundCodeDto(401, "GF", 1),
                new AipActivityFundCodeDto(401, "20% DF", 2),
                new AipActivityFundCodeDto(403, "GF", 5),
            ]);

        return new AipConsolidatedService(
            _aipRepo.Object, _expRepo.Object, _permissions.Object,
            NullLogger<AipConsolidatedService>.Instance);
    }

    private User Caller(bool crossOffice)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), Username = "u", PasswordHash = "h", FullName = "Test User",
            Role = UserRole.Staff, OfficeId = 8, IsActive = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _permissions.Setup(p => p.CanReviewAllOfficesAsync(u, It.IsAny<CancellationToken>()))
            .ReturnsAsync(crossOffice);
        return u;
    }

    private async Task<AipConsolidatedSheetDto> General()
    {
        ServiceResult<AipConsolidatedSheetDto> result =
            await Build().GetSheetAsync(FiscalYear, AipSector.General, Caller(crossOffice: true));
        Assert.True(result.IsSuccess, result.Error);
        return result.Value!;
    }

    // ── Which offices ─────────────────────────────────────────────────────────

    /// <summary>
    /// ⚠️ Decided 2026-09-14: offices not yet with PPDO are left out entirely — a sent-back one
    /// included — and their trees are never even loaded.
    /// </summary>
    [Fact]
    public async Task GetSheetAsync_OnlyOfficesWithPpdoOrAccepted_AreOnTheSheet()
    {
        AipConsolidatedSheetDto sheet = await General();

        Assert.Equal(
            ["OFFICE OF THE PROVINCIAL GOVERNOR", "PROVINCIAL PLANNING AND DEVELOPMENT OFFICE"],
            sheet.Rows.Where(r => r.Kind == AipFormRowBuilder.OfficeRow).Select(r => r.Name));
        Assert.DoesNotContain(sheet.Rows, r => r.Name == "Must never print");
        Assert.Equal([PgoGroup, PpdoGroup], _programsAskedFor!.Order());
    }

    // ── Rows ──────────────────────────────────────────────────────────────────

    /// <summary>Print order: office, then its program / project / activity rows by ref code; a synthetic project prints no row.</summary>
    [Fact]
    public async Task GetSheetAsync_Rows_FollowTheFormsOrderAndLevels()
    {
        AipConsolidatedSheetDto sheet = await General();

        Assert.Equal(
            [
                "Office:1000-000-1-01-001",
                "Program:1000-000-1-01-001-001",
                "Project:1000-000-1-01-001-001-001",
                "Activity:1000-000-1-01-001-001-001-001",
                "Activity:1000-000-1-01-001-001-001-002",
                "Office:1000-000-1-01-010",
                "Program:1000-000-1-01-010-001",
                "Activity:1000-000-1-01-010-001-001-001",
            ],
            sheet.Rows.Select(r => $"{r.Kind}:{r.RefCode}"));
    }

    /// <summary>
    /// A program with no PPAs under it — no project and no activity — is left off the sheet
    /// (2026-09-14), and so is one whose only project is synthetic and empty. The exact-order test
    /// above pins the same thing; this one names it.
    /// </summary>
    [Fact]
    public async Task GetSheetAsync_ProgramsWithNoPpas_AreLeftOff()
    {
        AipConsolidatedSheetDto sheet = await General();

        Assert.DoesNotContain(sheet.Rows, r => r.Name is "EMPTY PROGRAM" or "HOLLOW PROGRAM");
        Assert.Equal(2, sheet.Rows.Count(r => r.Kind == AipFormRowBuilder.ProgramRow));
    }

    /// <summary>Program and project rows carry no amounts — null, so nothing can print a zero there.</summary>
    [Fact]
    public async Task GetSheetAsync_ProgramAndProjectRows_HaveNoAmounts()
    {
        AipConsolidatedSheetDto sheet = await General();

        Assert.All(
            sheet.Rows.Where(r => r.Kind is AipFormRowBuilder.ProgramRow or AipFormRowBuilder.ProjectRow),
            r => Assert.Null(r.Amounts));
    }

    /// <summary>The office row is the sum of its activities' printed figures; TOTAL the sum of the office rows.</summary>
    [Fact]
    public async Task GetSheetAsync_Totals_AreBuiltUpwardFromPrintedFigures()
    {
        AipConsolidatedSheetDto sheet = await General();

        AipPrintedAmountsDto pgo = sheet.Rows.Single(r => r.RefCode == "1000-000-1-01-001").Amounts!;
        Assert.Equal(2_000m, pgo.Ps);
        Assert.Equal(1_303_000m, pgo.Mooe);
        Assert.Equal(1_305_000m, pgo.Total);

        AipPrintedAmountsDto ppdo = sheet.Rows.Single(r => r.RefCode == "1000-000-1-01-010").Amounts!;
        Assert.Equal(2_000m, ppdo.Co);
        Assert.Equal(2_000m, ppdo.CcAdaptation);

        Assert.Equal(1_307_000m, sheet.Total.Total);
        Assert.Equal(pgo.Total + ppdo.Total, sheet.Total.Total);
    }

    /// <summary>Column (7): line fund codes joined in entry order, else the activity's own snapshot.</summary>
    [Fact]
    public async Task GetSheetAsync_FundingSource_JoinsLineCodesOrFallsBackToTheSnapshot()
    {
        AipConsolidatedSheetDto sheet = await General();

        Assert.Equal("GF/20% DF", sheet.Rows.Single(r => r.ActivityId == 401).FundingSource);
        Assert.Equal("GF", sheet.Rows.Single(r => r.ActivityId == 402).FundingSource);
        Assert.Equal("ID", sheet.Rows.Single(r => r.ActivityId == 401).EsreCode);
    }

    [Fact]
    public async Task GetSheetAsync_OfficeRows_CarryTheirWorkflowStatus()
    {
        AipConsolidatedSheetDto sheet = await General();

        Assert.Equal(AipWorkflowStatus.Consolidated,
            sheet.Rows.Single(r => r.RefCode == "1000-000-1-01-010").WorkflowStatus);
    }

    // ── Counts ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Distinct offices, not group rows: office 7 heads a GENERAL and a SOCIAL group and counts once
    /// overall. A sent-back office is counted as not submitted.
    /// </summary>
    [Fact]
    public async Task GetSheetAsync_Counts_AreDistinctOfficesPerSectorAndOverall()
    {
        AipConsolidatedSheetDto sheet = await General();

        Assert.Equal(2, sheet.SubmittedOffices);
        Assert.Equal(5, sheet.TotalOffices);
        Assert.Equal(
            ["GENERAL 2/4", "SOCIAL 1/1", "ECONOMIC 0/1", "OTHERS 0/0"],
            sheet.Sectors.Select(s => $"{s.Sector} {s.SubmittedOffices}/{s.TotalOffices}"));
    }

    // ── Refusals and edges ────────────────────────────────────────────────────

    /// <summary>⚠️ The gate is the reviewer flag — a PPDO division user without it is refused.</summary>
    [Fact]
    public async Task GetSheetAsync_CallerWithoutCrossOfficeFlag_IsForbidden()
    {
        ServiceResult<AipConsolidatedSheetDto> result =
            await Build().GetSheetAsync(FiscalYear, AipSector.General, Caller(crossOffice: false));

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
        Assert.Null(_programsAskedFor);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("GENERAL PUBLIC SERVICES")]
    public async Task GetSheetAsync_UnknownSector_IsBadRequest(string? sector)
    {
        ServiceResult<AipConsolidatedSheetDto> result =
            await Build().GetSheetAsync(FiscalYear, sector, Caller(crossOffice: true));

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task GetSheetAsync_SectorIsCaseInsensitive()
    {
        ServiceResult<AipConsolidatedSheetDto> result =
            await Build().GetSheetAsync(FiscalYear, "social", Caller(crossOffice: true));

        Assert.True(result.IsSuccess);
        Assert.Equal(AipSector.Social, result.Value!.Sector);
        Assert.Equal(["OFFICE OF THE PROVINCIAL GOVERNOR - AKAP-HUB"],
            result.Value.Rows.Where(r => r.Kind == AipFormRowBuilder.OfficeRow).Select(r => r.Name));
    }

    /// <summary>A sector nobody has submitted in is an empty sheet with zero totals, and loads no tree.</summary>
    [Fact]
    public async Task GetSheetAsync_SectorWithNothingSubmitted_IsEmptyAndLoadsNoTree()
    {
        ServiceResult<AipConsolidatedSheetDto> result =
            await Build().GetSheetAsync(FiscalYear, AipSector.Economic, Caller(crossOffice: true));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value!.Rows);
        Assert.Equal(AipPrintedFigures.Zero, result.Value.Total);
        Assert.Null(_programsAskedFor);
    }

    /// <summary>An unopened year is an empty sheet, not a 404.</summary>
    [Fact]
    public async Task GetSheetAsync_UnopenedYear_IsAnEmptyUnopenedSheet()
    {
        _recordExists = false;

        ServiceResult<AipConsolidatedSheetDto> result =
            await Build().GetSheetAsync(FiscalYear, AipSector.General, Caller(crossOffice: true));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.Opened);
        Assert.Empty(result.Value.Rows);
        Assert.Equal(4, result.Value.Sectors.Count);
    }
}
