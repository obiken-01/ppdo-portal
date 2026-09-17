using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ExternalAipReadService"/> (v1.8.0 — PPDO-14, build spec §11). Covers
/// the release/pending split, legacy-vs-Fy2028 shaping, money formatting, the printed-amounts
/// pass-through, releasedAt sourcing, and office scoping.
/// </summary>
public sealed class ExternalAipReadServiceTests
{
    private const int Fy2027 = 2027;
    private const int Fy2028 = 2028;
    private const int PpdoOfficeId = 1;

    private static AipRecord Record(int id, int fiscalYear, string status) =>
        new() { Id = id, FiscalYear = fiscalYear, Status = status, EntrySource = "Manual", UploadedAt = DateTime.UtcNow };

    private static AipOffice Group(
        int id, int aipRecordId, int? officeId, string refCode, string name,
        string sector = "GENERAL", string workflowStatus = AipWorkflowStatus.Consolidated) => new()
    {
        Id = id, AipRecordId = aipRecordId, OfficeId = officeId, RefCode = refCode, Name = name,
        Sector = sector, WorkflowStatus = workflowStatus,
    };

    private static AipProgram Program(int id, int officeId, string refCode, string name) =>
        new() { Id = id, OfficeId = officeId, RefCode = refCode, Name = name };

    private static AipProject Project(int id, int programId, string refCode, string name, bool synthetic = false) =>
        new() { Id = id, ProgramId = programId, RefCode = refCode, Name = name, IsSynthetic = synthetic };

    private static AipActivity Activity(
        int id, int projectId, string refCode, string name,
        decimal? ps = null, decimal? mooe = null, decimal? co = null, bool synthetic = false) => new()
    {
        Id = id, ProjectId = projectId, RefCode = refCode, Name = name,
        Ps = ps, Mooe = mooe, Co = co, IsSynthetic = synthetic,
    };

    private static Office ConfigOffice(int id, string code, string name) =>
        new() { Id = id, OfficeCode = code, OfficeName = name, IsActive = true };

    private sealed class Fixture
    {
        public Mock<IAipRepository> Aip { get; } = new();
        public Mock<IAipExpenditureRepository> Expenditures { get; } = new();
        public Mock<IAuditRepository> Audit { get; } = new();
        public Mock<IOfficeRepository> Offices { get; } = new();
        public Mock<IRepository<FundingSource>> FundingSources { get; } = new();
        public Mock<IPriceIndexItemRepository> PriceIndexItems { get; } = new();

        public Fixture()
        {
            Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AipProgram>());
            Aip.Setup(a => a.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AipProject>());
            Aip.Setup(a => a.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AipActivity>());
            Expenditures.Setup(e => e.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AipExpenditure>());
            Expenditures.Setup(e => e.GetProcurementItemsByExpenditureIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AipProcurementItem>());
            Audit.Setup(a => a.GetByRecordIdsAsync(
                    It.IsAny<string>(), It.IsAny<IReadOnlyList<int>>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<AuditLog>());
            Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Office>());
            FundingSources.Setup(f => f.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<FundingSource>());
            PriceIndexItems.Setup(p => p.GetByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<PriceIndexItem>());
        }

        public ExternalAipReadService Build() => new(
            Aip.Object, Expenditures.Object, Audit.Object, Offices.Object,
            FundingSources.Object, PriceIndexItems.Object);
    }

    // ── data: null cases ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_YearNotOpened_ReturnsNull()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(2030, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AipRecord?)null);

        Assert.Null(await f.Build().GetAsync(2030, null));
    }

    [Fact]
    public async Task GetAsync_LegacyRecordStillDraft_ReturnsNull()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Draft));

        Assert.Null(await f.Build().GetAsync(Fy2027, null));
    }

    // ── Legacy format ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_LegacyFinalRecord_ReturnsLegacyFormat_StatusFinal_NoReleasedAt_NoPrintedTotals()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Final));
        AipOffice group = Group(10, 1, PpdoOfficeId, "1000-000-1-01-010", "PPDO");
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([group]);
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProgram>());
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office")]);

        ExternalAipDto? result = await f.Build().GetAsync(Fy2027, null);

        Assert.NotNull(result);
        Assert.Equal(ExternalAipConstants.FormatLegacy, result!.AipFormat);
        Assert.Single(result.Offices);
        Assert.Empty(result.PendingOffices);
        Assert.Equal(ExternalAipConstants.StatusFinal, result.Offices[0].Status);
        Assert.Null(result.Offices[0].ReleasedAt);
        Assert.Null(result.Offices[0].PrintedTotals);
        Assert.Null(result.PrintedTotals);
    }

    [Fact]
    public async Task GetAsync_LegacyOfficeWithNoConfigLink_IsExcluded()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Final));
        // OfficeId null — the unmatched legacy row the spec says to exclude.
        AipOffice unlinked = Group(10, 1, officeId: null, "1000-000-1-01-999", "Unmatched");
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([unlinked]);
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProgram>());

        ExternalAipDto? result = await f.Build().GetAsync(Fy2027, null);

        Assert.NotNull(result);
        Assert.Empty(result!.Offices);
        Assert.Empty(result.PendingOffices);
    }

    // ── Fy2028 release determination ─────────────────────────────────────────

    [Fact]
    public async Task GetAsync_Fy2028_AllGroupsConsolidated_OfficeIsReleased()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(2, Fy2028, PlanningStatus.Draft)); // record.Status irrelevant for Fy2028+
        AipOffice g1 = Group(20, 2, PpdoOfficeId, "1000-000-1-01-010", "PPDO", workflowStatus: AipWorkflowStatus.Consolidated);
        AipOffice g2 = Group(21, 2, PpdoOfficeId, "3000-000-1-01-010", "PPDO", sector: "SOCIAL", workflowStatus: AipWorkflowStatus.Consolidated);
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([g1, g2]);
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProgram>());
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office")]);

        ExternalAipDto? result = await f.Build().GetAsync(Fy2028, null);

        Assert.NotNull(result);
        Assert.Equal(ExternalAipConstants.FormatFy2028, result!.AipFormat);
        Assert.Single(result.Offices);
        Assert.Equal(2, result.Offices[0].Groups.Count); // two groups, keyed by (sector, name) — never merged
        Assert.Empty(result.PendingOffices);
    }

    [Fact]
    public async Task GetAsync_Fy2028_OneGroupNotConsolidated_OfficeIsPending_NeverPartial()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(2, Fy2028, PlanningStatus.Draft));
        AipOffice consolidated = Group(20, 2, PpdoOfficeId, "1000-000-1-01-010", "PPDO", workflowStatus: AipWorkflowStatus.Consolidated);
        AipOffice notYet = Group(21, 2, PpdoOfficeId, "3000-000-1-01-010", "PPDO", sector: "SOCIAL", workflowStatus: AipWorkflowStatus.SubmittedToPpdo);
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([consolidated, notYet]);
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProgram>());
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office")]);

        ExternalAipDto? result = await f.Build().GetAsync(Fy2028, null);

        Assert.NotNull(result);
        Assert.Empty(result!.Offices); // never a partial office
        Assert.Single(result.PendingOffices);
        Assert.Equal("PPDO", result.PendingOffices[0].Code);
    }

    [Fact]
    public async Task GetAsync_Fy2028_ReleasedAt_UsesLatestAcceptPpdoAcrossAllGroups()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(2, Fy2028, PlanningStatus.Draft));
        AipOffice group = Group(20, 2, PpdoOfficeId, "1000-000-1-01-010", "PPDO");
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([group]);
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProgram>());
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office")]);

        DateTime older = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime latest = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc); // after a return-and-resubmit
        f.Audit.Setup(a => a.GetByRecordIdsAsync(
                "aip_offices", It.Is<IReadOnlyList<int>>(ids => ids.Contains(20)),
                It.Is<IReadOnlyList<string>>(actions => actions.Contains(AuditAction.AcceptByPpdo)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new AuditLog { Id = 1, TableName = "aip_offices", RecordId = 20, Action = AuditAction.AcceptByPpdo, ChangedAt = older, ChangedById = Guid.NewGuid() },
                new AuditLog { Id = 2, TableName = "aip_offices", RecordId = 20, Action = AuditAction.AcceptByPpdo, ChangedAt = latest, ChangedById = Guid.NewGuid() },
            ]);

        ExternalAipDto? result = await f.Build().GetAsync(Fy2028, null);

        Assert.NotNull(result);
        Assert.NotNull(result!.Offices[0].ReleasedAt);
        Assert.StartsWith(latest.ToString("yyyy-MM-dd"), result.Offices[0].ReleasedAt);
        Assert.EndsWith("+08:00", result.Offices[0].ReleasedAt);
    }

    // ── Money + printed amounts ───────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_Amounts_AreTwoDecimalStrings()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Final));
        AipOffice group = Group(10, 1, PpdoOfficeId, "1000-000-1-01-010", "PPDO");
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([group]);
        AipProgram program = Program(100, 10, "1000-000-1-01-010-001", "Program A");
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([program]);
        AipProject project = Project(200, 100, "1000-000-1-01-010-001-001", "Project A");
        f.Aip.Setup(a => a.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([project]);
        AipActivity activity = Activity(300, 200, "1000-000-1-01-010-001-001-001", "Activity A", ps: 500m, mooe: 250.5m, co: 0m);
        f.Aip.Setup(a => a.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([activity]);
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office")]);

        ExternalAipDto? result = await f.Build().GetAsync(Fy2027, null);

        ExternalActivityDto mappedActivity = result!.Offices[0].Groups[0].Programs[0].Projects[0].Activities[0];
        Assert.Equal("500.00", mappedActivity.Amounts.Ps);
        Assert.Equal("250.50", mappedActivity.Amounts.Mooe);
        Assert.Equal("0.00", mappedActivity.Amounts.Co);
        Assert.Equal("750.50", mappedActivity.Amounts.Total);

        ExternalProjectDto mappedProject = result!.Offices[0].Groups[0].Programs[0].Projects[0];
        Assert.Equal("500.00", mappedProject.Totals.Ps);
        Assert.Equal("250.50", mappedProject.Totals.Mooe);
        Assert.Equal("0.00", mappedProject.Totals.Co);
        Assert.Equal("750.50", mappedProject.Totals.Total);

        ExternalProgramDto mappedProgram = result!.Offices[0].Groups[0].Programs[0];
        Assert.Equal("500.00", mappedProgram.Totals.Ps);
        Assert.Equal("250.50", mappedProgram.Totals.Mooe);
        Assert.Equal("0.00", mappedProgram.Totals.Co);
        Assert.Equal("750.50", mappedProgram.Totals.Total);
    }

    [Fact]
    public async Task GetAsync_PrintedAmounts_MatchesAcceptanceChecklistExample()
    {
        // From the build spec's own acceptance checklist: an activity with ₱1,000,400 MOOE shows
        // "1000400.00" in amounts and "1301000.00" in printedAmounts (×1.3, rounded up to the
        // thousand: 1,000,400 × 1.3 = 1,300,520 → 1,301,000).
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(2, Fy2028, PlanningStatus.Draft));
        AipOffice group = Group(20, 2, PpdoOfficeId, "1000-000-1-01-010", "PPDO");
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync([group]);
        AipProgram program = Program(100, 20, "1000-000-1-01-010-001", "Program A");
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([program]);
        AipProject project = Project(200, 100, "1000-000-1-01-010-001-001", "Project A");
        f.Aip.Setup(a => a.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([project]);
        AipActivity activity = Activity(300, 200, "1000-000-1-01-010-001-001-001", "Activity A", ps: 0m, mooe: 1000400m, co: 0m);
        f.Aip.Setup(a => a.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([activity]);
        AipExpenditure line = new()
        {
            Id = 400, ActivityId = 300, Mooe = 1000400m, Ps = 0m, Co = 0m,
            FundingSourceSnapshot = "GF", FundingSourceNameSnapshot = "General Fund",
        };
        f.Expenditures.Setup(e => e.GetByActivityIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([line]);
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office")]);

        ExternalAipDto? result = await f.Build().GetAsync(Fy2028, null);

        ExternalActivityDto mapped = result!.Offices[0].Groups[0].Programs[0].Projects[0].Activities[0];
        Assert.Equal("1000400.00", mapped.Amounts.Mooe);
        Assert.NotNull(mapped.PrintedAmounts);
        Assert.Equal("1301000.00", mapped.PrintedAmounts!.Mooe);
    }

    // ── isSynthetic ────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_SyntheticProjectAndActivity_FlagsCarryThrough()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Final));
        AipOffice group = Group(10, 1, PpdoOfficeId, "1000-000-1-01-010", "PPDO");
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([group]);
        AipProgram program = Program(100, 10, "1000-000-1-01-010-001", "Program A");
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([program]);
        AipProject syntheticProject = Project(200, 100, "1000-000-1-01-010-001", "Program A", synthetic: true);
        f.Aip.Setup(a => a.GetProjectsByProgramIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([syntheticProject]);
        AipActivity syntheticActivity = Activity(
            300, 200, "1000-000-1-01-010-001", "Program A", ps: 100m, mooe: 0m, co: 0m, synthetic: true);
        f.Aip.Setup(a => a.GetActivitiesByProjectIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([syntheticActivity]);
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office")]);

        ExternalAipDto? result = await f.Build().GetAsync(Fy2027, null);

        ExternalProjectDto project = result!.Offices[0].Groups[0].Programs[0].Projects[0];
        Assert.True(project.IsSynthetic);
        Assert.True(project.Activities[0].IsSynthetic);
    }

    // ── Office scoping ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_ScopedToOneOffice_EchoesOfficeCode_AndReturnsOnlyThatOffice()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Final));
        AipOffice ppdoGroup = Group(10, 1, PpdoOfficeId, "1000-000-1-01-010", "PPDO");
        AipOffice peoGroup = Group(11, 1, 2, "1000-000-1-01-020", "PEO");
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync([ppdoGroup, peoGroup]);
        f.Aip.Setup(a => a.GetProgramsByOfficeIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<AipProgram>());
        f.Offices.Setup(o => o.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office"), ConfigOffice(2, "PEO", "PEO Office")]);

        Office filterOffice = ConfigOffice(PpdoOfficeId, "PPDO", "PPDO Office");
        ExternalAipDto? result = await f.Build().GetAsync(Fy2027, filterOffice);

        Assert.NotNull(result);
        Assert.Equal("PPDO", result!.OfficeCode);
        Assert.Single(result.Offices);
        Assert.Equal("PPDO", result.Offices[0].Office.Code);
    }

    [Fact]
    public async Task GetAsync_WholeYear_OfficeCodeIsNull()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Final));
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<AipOffice>());

        ExternalAipDto? result = await f.Build().GetAsync(Fy2027, null);

        Assert.NotNull(result);
        Assert.Null(result!.OfficeCode);
    }

    // ── /aip/fiscal-years ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFiscalYearsAsync_OnlyReturnsYearsWithAtLeastOneReleasedOffice()
    {
        Fixture f = new();
        f.Aip.Setup(a => a.GetDistinctFiscalYearsAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Fy2028, Fy2027]);

        // FY2028: record exists but nothing consolidated yet — not released.
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2028, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(2, Fy2028, PlanningStatus.Draft));
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Group(20, 2, PpdoOfficeId, "1000-000-1-01-010", "PPDO", workflowStatus: AipWorkflowStatus.SubmittedToPpdo)]);

        // FY2027: Final legacy record — released.
        f.Aip.Setup(a => a.GetLatestByFiscalYearAsync(Fy2027, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Record(1, Fy2027, PlanningStatus.Final));
        f.Aip.Setup(a => a.GetOfficesByAipIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Group(10, 1, PpdoOfficeId, "1000-000-1-01-010", "PPDO")]);

        IReadOnlyList<int> years = await f.Build().GetFiscalYearsAsync(null);

        Assert.Equal([Fy2027], years);
    }
}
