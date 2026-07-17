using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// WFP Report preview (RAL-132). See <see cref="IWfpReportService"/> for the contract.
///
/// GetReportAsync walks the office's AIP hierarchy (program -> project -> activity), then for
/// each activity fetches every division's WfpExpenditure rows via
/// <see cref="IWfpExpenditureService.GetByActivityIdAsync"/> — the same computed totals the
/// entry wizard already saved, never re-derived here. This is N+1 (one call per wfp_activity
/// row, itself one call per activity per division that has started a WFP) — acceptable for an
/// office's activity count today; if this becomes a bottleneck, add a batched repository method
/// rather than hand-rolling SQL here.
///
/// The WFP FINAL sheet repeats its whole header/hierarchy/totals structure once per fund
/// source (a separate block for e.g. "5% GAD Fund" after the "General Fund" block) rather than
/// mixing funds into one table — expenditures are fetched once per activity, then the same
/// hierarchy-building pass runs once per distinct fund source name present, filtering each
/// activity's expenditures to that fund.
/// </summary>
public sealed class WfpReportService : IWfpReportService
{
    private const string UnassignedFunctionBand = "UNASSIGNED";
    private const string DefaultFundSourceName = "GENERAL FUND";

    private static readonly Dictionary<string, string> FunctionBandLabels = new()
    {
        ["CORE"] = "CORE FUNCTIONS",
        ["STRATEGIC"] = "STRATEGIC FUNCTIONS",
        ["SUPPORT"] = "SUPPORT FUNCTIONS",
        [UnassignedFunctionBand] = "UNASSIGNED FUNCTIONS",
    };

    private static readonly Dictionary<string, string> ExpenseClassLabels = new()
    {
        ["PS"] = "PERSONAL SERVICES",
        ["MOOE"] = "MAINTENANCE AND OTHER OPERATING EXPENSES",
        ["CO"] = "CAPITAL OUTLAY",
    };

    private static readonly string[] ExpenseClassOrder = ["PS", "MOOE", "CO"];

    /// <summary>AipOffice.Sector ("GENERAL"/"SOCIAL"/"ECONOMIC"/"OTHERS") -> the WFP FINAL sheet's exact label.</summary>
    private static readonly Dictionary<string, string> SectorLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GENERAL"] = "GENERAL PUBLIC SERVICES",
        ["SOCIAL"] = "SOCIAL SERVICES",
        ["ECONOMIC"] = "ECONOMIC SERVICES",
        ["OTHERS"] = "OTHER SERVICES",
    };

    private readonly IAipRepository          _aipRepo;
    private readonly IWfpRepository          _wfpRepo;
    private readonly IWfpExpenditureService  _expenditures;
    private readonly IRepository<Office>     _officeRepo;
    private readonly IRepository<Account>    _accountRepo;

    public WfpReportService(
        IAipRepository          aipRepo,
        IWfpRepository          wfpRepo,
        IWfpExpenditureService  expenditures,
        IRepository<Office>     officeRepo,
        IRepository<Account>    accountRepo)
    {
        _aipRepo      = aipRepo;
        _wfpRepo      = wfpRepo;
        _expenditures = expenditures;
        _officeRepo   = officeRepo;
        _accountRepo  = accountRepo;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Computed directly here rather than via <see cref="IBudgetPlanningDashboardService"/>
    /// (v1.4.5 — RAL-161): the Dashboard is now permanently scoped to the PPDO office, but this
    /// picker legitimately spans every office with a WFP, so it can no longer reuse the
    /// Dashboard's (now PPDO-only) WfpByOffice — same underlying "one representative record per
    /// office, prefer Final" logic, just resolved independently.
    /// </remarks>
    public async Task<IReadOnlyList<WfpReportOfficeDto>> GetEligibleOfficesAsync(
        int fiscalYear, CancellationToken cancellationToken = default)
    {
        AipRecord? primaryAip = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, cancellationToken);
        if (primaryAip is null) return [];

        IReadOnlyList<WfpRecord> records =
            await _wfpRepo.GetFilteredAsync(primaryAip.Id, officeId: null, divisionId: null, cancellationToken);
        Dictionary<int, WfpRecord> byOffice = records
            .GroupBy(w => w.OfficeId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(w => w.Status == PlanningStatus.Final)
                      .ThenByDescending(w => w.UpdatedAt)
                      .First());

        IReadOnlyList<Office> activeOffices = (await _officeRepo.GetAllAsync(cancellationToken))
            .Where(o => o.IsActive)
            .ToList();

        return activeOffices
            .Where(o => byOffice.ContainsKey(o.Id))
            .Select(o => new WfpReportOfficeDto(o.Id, o.OfficeCode, o.OfficeName, byOffice[o.Id].Status))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<WfpReportDto>> GetReportAsync(
        int officeId, int fiscalYear, int? divisionId = null, CancellationToken cancellationToken = default)
    {
        Office? office = (await _officeRepo.GetAllAsync(cancellationToken))
            .FirstOrDefault(o => o.Id == officeId);
        if (office is null)
            return ServiceResult<WfpReportDto>.NotFound($"Office {officeId} not found.");
        if (string.IsNullOrWhiteSpace(office.OfficeRefCode))
            return ServiceResult<WfpReportDto>.NotFound(
                $"Office {office.OfficeName} has no AIP reference code configured.");

        // Primary AIP for the fiscal year: prefer Final, then Draft, then newest — same rule
        // BudgetPlanningDashboardService uses so "the office's current AIP" means the same
        // thing everywhere in Budget Planning.
        static int AipStatusRank(string s) => s == "Final" ? 0 : s == "Draft" ? 1 : 2;
        AipRecord? aipRecord = (await _aipRepo.GetAllAsync(cancellationToken))
            .Where(a => a.FiscalYear == fiscalYear && a.Status != PlanningStatus.Archived)
            .OrderBy(a => AipStatusRank(a.Status))
            .ThenByDescending(a => a.Id)
            .FirstOrDefault();
        if (aipRecord is null)
            return ServiceResult<WfpReportDto>.NotFound($"No AIP found for fiscal year {fiscalYear}.");

        List<AipOffice> aipOffices = (await _aipRepo.GetOfficesByAipIdAsync(aipRecord.Id, cancellationToken))
            .Where(o => o.RefCode.EndsWith(office.OfficeRefCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (aipOffices.Count == 0)
            return ServiceResult<WfpReportDto>.NotFound(
                $"No AIP hierarchy found for {office.OfficeName} under FY {fiscalYear}.");

        Dictionary<int, string> sectorLabelByAipOfficeId = aipOffices.ToDictionary(
            o => o.Id, o => SectorLabels.GetValueOrDefault(o.Sector, o.Sector));

        List<int> aipOfficeIds = aipOffices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsAsync(aipOfficeIds, cancellationToken);
        List<int> programIds = programs.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject> projects = await _aipRepo.GetProjectsByProgramIdsAsync(programIds, cancellationToken);
        List<int> projectIds = projects.Select(p => p.Id).ToList();
        IReadOnlyList<AipActivity> activities = await _aipRepo.GetActivitiesByProjectIdsAsync(projectIds, cancellationToken);

        // AipActivityId -> every wfp_activity row for it. When divisionId is null this spans
        // ALL of the office's divisions (a WfpRecord is scoped to one division; the report
        // merges them — WfpRecord.cs); when provided, only that division's WfpRecord (RAL-136).
        Dictionary<int, List<int>> wfpActivityIdsByAipActivityId = await BuildWfpActivityMapAsync(
            aipRecord.Id, officeId, divisionId, cancellationToken);

        Dictionary<int, Account> accountsById = (await _accountRepo.GetAllAsync(cancellationToken))
            .ToDictionary(a => a.Id);

        // Fetch every activity's expenditures ONCE (not once per fund source) — each fund
        // source's hierarchy pass below filters this same in-memory list. Batched across every
        // division's wfp_activity in a fixed number of queries rather than one-per-activity, and
        // one-per-expenditure for its periods/items (RAL-158) — the report only ever reads these.
        List<int> allWfpActivityIds = wfpActivityIdsByAipActivityId.Values.SelectMany(ids => ids).ToList();
        IReadOnlyDictionary<int, IReadOnlyList<WfpExpenditureDto>> expendituresByWfpActivityId =
            await _expenditures.GetByActivityIdsAsync(allWfpActivityIds, cancellationToken);

        Dictionary<int, List<WfpExpenditureDto>> expendituresByAipActivityId = [];
        foreach (AipActivity activity in activities)
        {
            List<int> wfpActivityIds = wfpActivityIdsByAipActivityId.GetValueOrDefault(activity.Id, []);
            List<WfpExpenditureDto> expenditures = [];
            foreach (int wfpActivityId in wfpActivityIds)
                if (expendituresByWfpActivityId.TryGetValue(wfpActivityId, out IReadOnlyList<WfpExpenditureDto>? e))
                    expenditures.AddRange(e);
            if (expenditures.Count > 0)
                expendituresByAipActivityId[activity.Id] = expenditures;
        }

        List<string> fundSourceNames = expendituresByAipActivityId.Values
            .SelectMany(x => x)
            .Select(FundSourceNameFor)
            .Distinct()
            .OrderBy(n => n.Equals(DefaultFundSourceName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<WfpReportFundSourceDto> fundSourceReports = fundSourceNames
            .Select(fundSourceName =>
            {
                List<WfpReportFunctionBandSectionDto> sections = BuildSections(fundSourceName, programs, projects,
                    activities, expendituresByAipActivityId, accountsById, sectorLabelByAipOfficeId);
                // Breakdown is per FUND SOURCE, not per section — it appears once, after every
                // CORE/STRATEGIC/SUPPORT section has been listed (confirmed against the
                // reference sheet), so it's built from ALL of this fund's sections combined.
                return new WfpReportFundSourceDto(fundSourceName, sections, BuildBreakdown(sections));
            })
            .ToList();

        return ServiceResult<WfpReportDto>.Ok(new WfpReportDto(
            fiscalYear, office.OfficeCode, office.OfficeName, WfpReserveRule.Rate, fundSourceReports));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<int, List<int>>> BuildWfpActivityMapAsync(
        int aipRecordId, int officeId, int? divisionId, CancellationToken ct)
    {
        IReadOnlyList<WfpRecord> wfpRecords = await _wfpRepo.GetFilteredAsync(aipRecordId, officeId, divisionId, ct);
        if (wfpRecords.Count == 0) return [];

        // One query for every division's activities instead of one-per-record (RAL-158).
        IReadOnlyList<WfpActivity> wfpActivities = await _wfpRepo.GetActivitiesByWfpIdsAsync(
            wfpRecords.Select(r => r.Id).ToList(), ct);

        Dictionary<int, List<int>> map = [];
        foreach (WfpActivity wfpActivity in wfpActivities)
        {
            if (!map.TryGetValue(wfpActivity.AipActivityId, out List<int>? ids))
                map[wfpActivity.AipActivityId] = ids = [];
            ids.Add(wfpActivity.Id);
        }
        return map;
    }

    /// <summary>Builds the function-band -> program -> project -> activity hierarchy for ONE fund source's expenditures only.</summary>
    private static List<WfpReportFunctionBandSectionDto> BuildSections(
        string fundSourceName,
        IReadOnlyList<AipProgram> programs,
        IReadOnlyList<AipProject> projects,
        IReadOnlyList<AipActivity> activities,
        IReadOnlyDictionary<int, List<WfpExpenditureDto>> expendituresByAipActivityId,
        IReadOnlyDictionary<int, Account> accountsById,
        IReadOnlyDictionary<int, string> sectorLabelByAipOfficeId)
    {
        Dictionary<int, List<WfpReportProjectDto>> projectDtosByProgramId = [];
        foreach (AipProject project in projects.OrderBy(p => RefCodeSequence(p.RefCode)).ThenBy(p => p.RefCode))
        {
            AipProgram? parentProgram = programs.FirstOrDefault(p => p.Id == project.ProgramId);
            string sector = parentProgram is not null
                ? sectorLabelByAipOfficeId.GetValueOrDefault(parentProgram.OfficeId, "")
                : "";

            List<WfpReportActivityDto> activityDtos = [];
            foreach (AipActivity activity in activities.Where(a => a.ProjectId == project.Id)
                         .OrderBy(a => RefCodeSequence(a.RefCode)).ThenBy(a => a.RefCode))
            {
                if (!expendituresByAipActivityId.TryGetValue(activity.Id, out List<WfpExpenditureDto>? allExpenditures))
                    continue;

                List<WfpExpenditureDto> expenditures = allExpenditures
                    .Where(e => FundSourceNameFor(e) == fundSourceName)
                    .ToList();
                // Skip activities with nothing entered under THIS fund — they simply don't
                // appear in this fund source's block (they may still appear in another one).
                if (expenditures.Count == 0) continue;

                activityDtos.Add(BuildActivityDto(activity, expenditures, accountsById, sector));
            }
            if (activityDtos.Count == 0) continue;

            WfpReportAmountsDto projectTotal = activityDtos.Aggregate(
                WfpReportAmountsDto.Zero, (acc, a) => acc + a.GrandTotal);
            WfpReportProjectDto projectDto = new(project.RefCode, project.Name, activityDtos, projectTotal);
            if (!projectDtosByProgramId.TryGetValue(project.ProgramId, out List<WfpReportProjectDto>? list))
                projectDtosByProgramId[project.ProgramId] = list = [];
            list.Add(projectDto);
        }

        Dictionary<string, List<WfpReportProgramDto>> programsByBand = [];
        foreach (AipProgram program in programs.OrderBy(p => RefCodeSequence(p.RefCode)).ThenBy(p => p.RefCode))
        {
            if (!projectDtosByProgramId.TryGetValue(program.Id, out List<WfpReportProjectDto>? projectDtos))
                continue;

            WfpReportAmountsDto programTotal = projectDtos.Aggregate(
                WfpReportAmountsDto.Zero, (acc, p) => acc + p.GrandTotal);
            WfpReportProgramDto programDto = new(program.RefCode, program.Name, projectDtos, programTotal);
            string band = string.IsNullOrWhiteSpace(program.FunctionBand)
                ? UnassignedFunctionBand
                : program.FunctionBand;
            if (!programsByBand.TryGetValue(band, out List<WfpReportProgramDto>? list))
                programsByBand[band] = list = [];
            list.Add(programDto);
        }

        string[] bandOrder = ["CORE", "STRATEGIC", "SUPPORT", UnassignedFunctionBand];
        return bandOrder
            .Where(programsByBand.ContainsKey)
            .Select(band => new WfpReportFunctionBandSectionDto(band, FunctionBandLabels[band], programsByBand[band]))
            .ToList();
    }

    private static WfpReportActivityDto BuildActivityDto(
        AipActivity activity,
        IReadOnlyList<WfpExpenditureDto> expenditures,
        IReadOnlyDictionary<int, Account> accountsById,
        string sector)
    {
        List<WfpReportExpenseClassGroupDto> groups = [];
        WfpReportAmountsDto grandTotal = WfpReportAmountsDto.Zero;

        List<string> classesPresent = expenditures
            .Select(e => ExpenseClassFor(e, accountsById))
            .Distinct()
            .OrderBy(c => Array.IndexOf(ExpenseClassOrder, c) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(c => c)
            .ToList();

        foreach (string expenseClass in classesPresent)
        {
            List<WfpReportRowDto> rows = expenditures
                .Where(e => ExpenseClassFor(e, accountsById) == expenseClass)
                .Select(e => ToRow(e, sector))
                .ToList();
            WfpReportAmountsDto subTotal = rows.Aggregate(WfpReportAmountsDto.Zero, (acc, r) => acc + r.Amounts);
            groups.Add(new WfpReportExpenseClassGroupDto(
                expenseClass, ExpenseClassLabels.GetValueOrDefault(expenseClass, expenseClass), rows, subTotal));
            grandTotal += subTotal;
        }

        return new WfpReportActivityDto(activity.RefCode, activity.Name, activity.IsCreation, groups, grandTotal);
    }

    /// <summary>
    /// Fund-source-closing breakdown (WFP FINAL sheet rows "TOTAL - PERSONAL SERVICES" through
    /// "GRAND-TOTAL") — every activity's expense-class sub-totals across ALL of this fund's
    /// sections (CORE + STRATEGIC + SUPPORT + UNASSIGNED combined), bucketed into PS / MOOE
    /// (excl. creation) / CO / PS-creation / MOOE-creation. GrandTotal is summed from the
    /// programs' own grand totals (not from the five buckets) so it stays correct even if an
    /// expenditure's account maps to neither PS, MOOE, nor CO (ExpenseClassFor's "OTHER"
    /// fallback) — that activity still counts toward the total, it just isn't itemised in one
    /// of the five labelled buckets.
    /// </summary>
    private static WfpReportBreakdownDto BuildBreakdown(IReadOnlyList<WfpReportFunctionBandSectionDto> sections)
    {
        IReadOnlyList<WfpReportProgramDto> allPrograms = sections.SelectMany(s => s.Programs).ToList();

        WfpReportAmountsDto ps = WfpReportAmountsDto.Zero, mooe = WfpReportAmountsDto.Zero, co = WfpReportAmountsDto.Zero;
        WfpReportAmountsDto psCreation = WfpReportAmountsDto.Zero, mooeCreation = WfpReportAmountsDto.Zero;

        foreach (WfpReportActivityDto activity in allPrograms.SelectMany(p => p.Projects).SelectMany(p => p.Activities))
        {
            foreach (WfpReportExpenseClassGroupDto group in activity.ExpenseClasses)
            {
                switch (group.ExpenseClass)
                {
                    case "PS":
                        if (activity.IsCreation) psCreation += group.SubTotal; else ps += group.SubTotal;
                        break;
                    case "MOOE":
                        if (activity.IsCreation) mooeCreation += group.SubTotal; else mooe += group.SubTotal;
                        break;
                    case "CO":
                        co += group.SubTotal;
                        break;
                    // "OTHER" (no account, or an account outside PS/MOOE/CO) has no labelled
                    // bucket in the sheet — deliberately excluded from the five totals below,
                    // but still folded into GrandTotal via the programs' totals.
                }
            }
        }

        WfpReportAmountsDto grandTotal = allPrograms.Aggregate(WfpReportAmountsDto.Zero, (acc, p) => acc + p.GrandTotal);
        return new WfpReportBreakdownDto(ps, mooe, co, psCreation, mooeCreation, grandTotal);
    }

    /// <summary>
    /// Parses a Program/Project/Activity's own sequence number — the last dash-separated segment
    /// of its ref code (e.g. "001" in "1000-000-1-01-010-002-001") — so siblings sort by AIP
    /// numbering (RAL-150) regardless of the order a user happened to enter/save WFP expenditures
    /// in. Falls back to <see cref="int.MaxValue"/> for a ref code that doesn't parse (sorts last,
    /// never crashes the report) — every real AIP ref code segment is numeric today.
    /// </summary>
    private static int RefCodeSequence(string refCode)
    {
        string lastSegment = refCode.Split('-')[^1];
        return int.TryParse(lastSegment, out int seq) ? seq : int.MaxValue;
    }

    private static string ExpenseClassFor(WfpExpenditureDto e, IReadOnlyDictionary<int, Account> accountsById) =>
        e.AccountId.HasValue && accountsById.TryGetValue(e.AccountId.Value, out Account? account)
            ? account.ExpenseClass
            : "OTHER";

    /// <summary>
    /// Fund source display name for grouping/section headers — upper-cased to match the WFP
    /// FINAL sheet's "SOURCE OF FUND: ..." convention, and so an expenditure with an explicit
    /// "General Fund" snapshot groups with one that has no fund source set at all (both would
    /// otherwise read as different casings of the same default fund).
    /// </summary>
    private static string FundSourceNameFor(WfpExpenditureDto e) =>
        (string.IsNullOrWhiteSpace(e.FundingSourceNameSnapshot) ? DefaultFundSourceName : e.FundingSourceNameSnapshot)
            .ToUpperInvariant();

    private static WfpReportRowDto ToRow(WfpExpenditureDto e, string sector) => new(
        sector,
        e.Nature,
        e.AccountNumberSnapshot,
        e.AccountTitleSnapshot,
        new WfpReportAmountsDto(
            e.TotalAppropriation, e.ReserveAmount, e.NetAppropriation,
            e.Q1, e.Q2, e.Q3, e.Q4, e.NetAppropriation));
}
