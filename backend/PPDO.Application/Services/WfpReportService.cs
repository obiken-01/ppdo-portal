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
/// </summary>
public sealed class WfpReportService : IWfpReportService
{
    private const string UnassignedFunctionBand = "UNASSIGNED";

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

        // Build bottom-up, keyed by integer PK throughout (RefCode is only unique within its
        // immediate parent, not globally, so it can't safely be used to re-associate rows).
        Dictionary<int, List<WfpReportProjectDto>> projectDtosByProgramId = [];
        foreach (AipProject project in projects.OrderBy(p => p.RefCode))
        {
            List<WfpReportActivityDto> activityDtos = [];
            foreach (AipActivity activity in activities.Where(a => a.ProjectId == project.Id).OrderBy(a => a.RefCode))
            {
                List<int> wfpActivityIds = wfpActivityIdsByAipActivityId.GetValueOrDefault(activity.Id, []);
                List<WfpExpenditureDto> expenditures = [];
                foreach (int wfpActivityId in wfpActivityIds)
                    expenditures.AddRange(await _expenditures.GetByActivityIdAsync(wfpActivityId, cancellationToken));

                // Skip activities with nothing entered yet — they aren't part of the WFP report
                // (the sheet only lists PPAs that actually have appropriations). This naturally
                // cascades: a project left with zero activities, or a program left with zero
                // projects, is dropped below too.
                if (expenditures.Count == 0) continue;

                activityDtos.Add(BuildActivityDto(activity, expenditures, accountsById));
            }
            if (activityDtos.Count == 0) continue;

            WfpReportProjectDto projectDto = new(project.RefCode, project.Name, activityDtos);
            if (!projectDtosByProgramId.TryGetValue(project.ProgramId, out List<WfpReportProjectDto>? list))
                projectDtosByProgramId[project.ProgramId] = list = [];
            list.Add(projectDto);
        }

        Dictionary<string, List<WfpReportProgramDto>> programsByBand = [];
        foreach (AipProgram program in programs.OrderBy(p => p.RefCode))
        {
            if (!projectDtosByProgramId.TryGetValue(program.Id, out List<WfpReportProjectDto>? projectDtos))
                continue;

            WfpReportProgramDto programDto = new(program.RefCode, program.Name, projectDtos);
            string band = string.IsNullOrWhiteSpace(program.FunctionBand)
                ? UnassignedFunctionBand
                : program.FunctionBand;
            if (!programsByBand.TryGetValue(band, out List<WfpReportProgramDto>? list))
                programsByBand[band] = list = [];
            list.Add(programDto);
        }

        string[] bandOrder = ["CORE", "STRATEGIC", "SUPPORT", UnassignedFunctionBand];
        List<WfpReportFunctionBandSectionDto> sections = bandOrder
            .Where(programsByBand.ContainsKey)
            .Select(band => new WfpReportFunctionBandSectionDto(
                band, FunctionBandLabels[band], programsByBand[band]))
            .ToList();

        return ServiceResult<WfpReportDto>.Ok(new WfpReportDto(
            fiscalYear, office.OfficeCode, office.OfficeName, WfpReserveRule.Rate, sections));
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

    private static WfpReportActivityDto BuildActivityDto(
        AipActivity activity,
        IReadOnlyList<WfpExpenditureDto> expenditures,
        IReadOnlyDictionary<int, Account> accountsById)
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
                .Select(ToRow)
                .ToList();
            WfpReportAmountsDto subTotal = rows.Aggregate(WfpReportAmountsDto.Zero, (acc, r) => acc + r.Amounts);
            groups.Add(new WfpReportExpenseClassGroupDto(
                expenseClass, ExpenseClassLabels.GetValueOrDefault(expenseClass, expenseClass), rows, subTotal));
            grandTotal += subTotal;
        }

        return new WfpReportActivityDto(activity.RefCode, activity.Name, activity.IsCreation, groups, grandTotal);
    }

    private static string ExpenseClassFor(WfpExpenditureDto e, IReadOnlyDictionary<int, Account> accountsById) =>
        e.AccountId.HasValue && accountsById.TryGetValue(e.AccountId.Value, out Account? account)
            ? account.ExpenseClass
            : "OTHER";

    private static WfpReportRowDto ToRow(WfpExpenditureDto e) => new(
        e.Nature,
        e.AccountNumberSnapshot,
        e.AccountTitleSnapshot,
        new WfpReportAmountsDto(
            e.TotalAppropriation, e.ReserveAmount, e.NetAppropriation,
            e.Q1, e.Q2, e.Q3, e.Q4, e.NetAppropriation));
}
