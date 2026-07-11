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

    private readonly IBudgetPlanningDashboardService _dashboard;
    private readonly IAipRepository                  _aipRepo;
    private readonly IWfpRepository                  _wfpRepo;
    private readonly IWfpExpenditureService          _expenditures;
    private readonly IRepository<Office>             _officeRepo;
    private readonly IRepository<Account>            _accountRepo;

    public WfpReportService(
        IBudgetPlanningDashboardService dashboard,
        IAipRepository                  aipRepo,
        IWfpRepository                  wfpRepo,
        IWfpExpenditureService          expenditures,
        IRepository<Office>             officeRepo,
        IRepository<Account>            accountRepo)
    {
        _dashboard    = dashboard;
        _aipRepo      = aipRepo;
        _wfpRepo      = wfpRepo;
        _expenditures = expenditures;
        _officeRepo   = officeRepo;
        _accountRepo  = accountRepo;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpReportOfficeDto>> GetEligibleOfficesAsync(
        int fiscalYear, CancellationToken cancellationToken = default)
    {
        PlanningDashboardDto dashboard = await _dashboard.GetDashboardAsync(fiscalYear, cancellationToken);
        return dashboard.WfpByOffice
            .Where(o => o.WfpStatus != "Not started")
            .Select(o => new WfpReportOfficeDto(o.OfficeId, o.OfficeCode, o.OfficeName, o.WfpStatus))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<WfpReportDto>> GetReportAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken = default)
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

        // AipActivityId -> every wfp_activity row for it, across ALL of the office's divisions
        // (a WfpRecord is scoped to one division; the report merges them — WfpRecord.cs).
        Dictionary<int, List<int>> wfpActivityIdsByAipActivityId = await BuildWfpActivityMapAsync(
            aipRecord.Id, officeId, cancellationToken);

        Dictionary<int, Account> accountsById = (await _accountRepo.GetAllAsync(cancellationToken))
            .ToDictionary(a => a.Id);

        // Fetch every activity's expenditures ONCE (not once per fund source) — each fund
        // source's hierarchy pass below filters this same in-memory list.
        Dictionary<int, List<WfpExpenditureDto>> expendituresByAipActivityId = [];
        foreach (AipActivity activity in activities)
        {
            List<int> wfpActivityIds = wfpActivityIdsByAipActivityId.GetValueOrDefault(activity.Id, []);
            List<WfpExpenditureDto> expenditures = [];
            foreach (int wfpActivityId in wfpActivityIds)
                expenditures.AddRange(await _expenditures.GetByActivityIdAsync(wfpActivityId, cancellationToken));
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
            .Select(fundSourceName => new WfpReportFundSourceDto(
                fundSourceName,
                BuildSections(fundSourceName, programs, projects, activities,
                    expendituresByAipActivityId, accountsById, sectorLabelByAipOfficeId)))
            .ToList();

        return ServiceResult<WfpReportDto>.Ok(new WfpReportDto(
            fiscalYear, office.OfficeCode, office.OfficeName, WfpReserveRule.Rate, fundSourceReports));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<int, List<int>>> BuildWfpActivityMapAsync(
        int aipRecordId, int officeId, CancellationToken ct)
    {
        IReadOnlyList<WfpRecord> wfpRecords = await _wfpRepo.GetFilteredAsync(aipRecordId, officeId, null, ct);

        Dictionary<int, List<int>> map = [];
        foreach (WfpRecord record in wfpRecords)
        {
            IReadOnlyList<WfpActivity> wfpActivities = await _wfpRepo.GetActivitiesByWfpIdAsync(record.Id, ct);
            foreach (WfpActivity wfpActivity in wfpActivities)
            {
                if (!map.TryGetValue(wfpActivity.AipActivityId, out List<int>? ids))
                    map[wfpActivity.AipActivityId] = ids = [];
                ids.Add(wfpActivity.Id);
            }
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
        foreach (AipProject project in projects.OrderBy(p => p.RefCode))
        {
            AipProgram? parentProgram = programs.FirstOrDefault(p => p.Id == project.ProgramId);
            string sector = parentProgram is not null
                ? sectorLabelByAipOfficeId.GetValueOrDefault(parentProgram.OfficeId, "")
                : "";

            List<WfpReportActivityDto> activityDtos = [];
            foreach (AipActivity activity in activities.Where(a => a.ProjectId == project.Id).OrderBy(a => a.RefCode))
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
        foreach (AipProgram program in programs.OrderBy(p => p.RefCode))
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
            .Select(band => new WfpReportFunctionBandSectionDto(
                band, FunctionBandLabels[band], programsByBand[band], BuildSectionBreakdown(programsByBand[band])))
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
    /// Section-closing breakdown (WFP FINAL sheet rows "TOTAL - PERSONAL SERVICES" through
    /// "GRAND-TOTAL"): every activity's expense-class sub-totals bucketed into PS / MOOE
    /// (excl. creation) / CO / PS-creation / MOOE-creation. GrandTotal is summed from the
    /// programs' own grand totals (not from the five buckets) so it stays correct even if an
    /// expenditure's account maps to neither PS, MOOE, nor CO (ExpenseClassFor's "OTHER"
    /// fallback) — that activity still counts toward the section total, it just isn't
    /// itemised in one of the five labelled buckets.
    /// </summary>
    private static WfpReportSectionBreakdownDto BuildSectionBreakdown(IReadOnlyList<WfpReportProgramDto> programs)
    {
        WfpReportAmountsDto ps = WfpReportAmountsDto.Zero, mooe = WfpReportAmountsDto.Zero, co = WfpReportAmountsDto.Zero;
        WfpReportAmountsDto psCreation = WfpReportAmountsDto.Zero, mooeCreation = WfpReportAmountsDto.Zero;

        foreach (WfpReportActivityDto activity in programs.SelectMany(p => p.Projects).SelectMany(p => p.Activities))
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

        WfpReportAmountsDto grandTotal = programs.Aggregate(WfpReportAmountsDto.Zero, (acc, p) => acc + p.GrandTotal);
        return new WfpReportSectionBreakdownDto(ps, mooe, co, psCreation, mooeCreation, grandTotal);
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
