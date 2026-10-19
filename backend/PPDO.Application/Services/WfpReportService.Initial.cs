using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Application.Services;

// The initial WFP, read off the consolidated AIP for an entered year (v1.9.0 — PPDO-224,
// docs/v1.9/Initial_WFP_Report_Spec.md). Nothing is stored or copied (decision 1): every request
// rebuilds the report from the AIP's lines, so a PPDO reviewer edit after consolidation shows at once.
public sealed partial class WfpReportService
{
    public const string ReportNotFoundMessage = "Report not found.";

    /// <summary>"PTO's FY 2028 AIP is not marked as done yet." (decision 5)</summary>
    public static string NotConsolidatedMessage(string office, int fiscalYear)
        => $"{office}'s FY {fiscalYear} AIP is not marked as done yet.";

    /// <summary>The same refusal, said to the office's own users (spec §3 case 12).</summary>
    public static string OwnNotConsolidatedMessage(int fiscalYear)
        => $"Your FY {fiscalYear} AIP is not marked as done yet.";

    /// <inheritdoc />
    public async Task<IReadOnlyList<WfpReportOfficeDto>> GetInitialEligibleOfficesAsync(
        int fiscalYear, User caller, CancellationToken cancellationToken = default)
    {
        AipRecord? record = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, cancellationToken);
        if (record is null) return [];

        // Decision 5: an office is offered only once EVERY one of its groups is Consolidated.
        HashSet<int> consolidated = (await _aipRepo.GetOfficesByAipIdNoTrackingAsync(record.Id, cancellationToken))
            .Where(g => g.OfficeId is not null)
            .GroupBy(g => g.OfficeId!.Value)
            .Where(g => g.All(o => o.WorkflowStatus == AipWorkflowStatus.Consolidated))
            .Select(g => g.Key)
            .ToHashSet();
        if (consolidated.Count == 0) return [];

        bool canAccessWfp = await _permissions.CanAccessWfpAsync(caller, cancellationToken);
        bool canAccessBudgetPlanning = await _permissions.CanAccessBudgetPlanningAsync(caller, cancellationToken);

        return (await _configCache.GetOfficesAsync(cancellationToken))
            .Where(o => o.IsActive && consolidated.Contains(o.Id)
                     && InitialWfpReadRule.Permits(caller, o.Id, canAccessWfp, canAccessBudgetPlanning))
            .Select(o => new WfpReportOfficeDto(o.Id, o.OfficeCode, o.OfficeName, AipWorkflowStatus.Consolidated))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<WfpReportDto>> GetInitialReportAsync(
        int officeId, int fiscalYear, int? requestedDivisionId, User caller, CancellationToken cancellationToken = default)
    {
        // ── Scope first: an office the caller may not read is as missing as one that does not exist ──
        Office? office = await _officeRepo.GetByIdAsync(officeId, cancellationToken);
        if (office is null || !await MayReadInitialReportAsync(caller, officeId, cancellationToken))
            return ServiceResult<WfpReportDto>.NotFound(ReportNotFoundMessage);

        AipRecord? record = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, cancellationToken);
        if (record is null)
            return ServiceResult<WfpReportDto>.NotFound($"No AIP found for fiscal year {fiscalYear}.");

        List<AipOffice> groups = (await _aipRepo.GetOfficesByAipIdNoTrackingAsync(record.Id, cancellationToken))
            .Where(g => g.OfficeId == officeId)
            .ToList();
        if (groups.Count == 0)
            return ServiceResult<WfpReportDto>.NotFound(
                $"No AIP hierarchy found for {office.OfficeName} under FY {fiscalYear}.");

        // ── The gate (decision 5) ──
        if (groups.Any(g => g.WorkflowStatus != AipWorkflowStatus.Consolidated))
            return ServiceResult<WfpReportDto>.Conflict(caller.OfficeId == officeId
                ? OwnNotConsolidatedMessage(fiscalYear)
                : NotConsolidatedMessage(office.OfficeCode, fiscalYear));

        // ── The division (decision 12, RAL-136) ──
        InitialDivision? division = await TryResolveInitialDivisionAsync(caller, officeId, requestedDivisionId, cancellationToken);
        if (division is null)
            return ServiceResult<WfpReportDto>.NotFound(ReportNotFoundMessage);

        // Sequential reads: one DbContext. A fixed number of queries, never one per activity or line.
        Dictionary<int, string> sectorLabelByAipOfficeId = groups.ToDictionary(
            o => o.Id, o => SectorLabels.GetValueOrDefault(o.Sector, o.Sector));
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(
            groups.Select(g => g.Id).ToList(), cancellationToken);
        IReadOnlyList<AipProject> projects = await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(
            programs.Select(p => p.Id).ToList(), cancellationToken);
        List<AipActivity> activities = (await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(
                projects.Select(p => p.Id).ToList(), cancellationToken))
            // Decision 12: by the activity's division tag; untagged work is whole-office only.
            .Where(a => division.DivisionId is not int d || a.DivisionId == d)
            .ToList();

        IReadOnlyList<AipExpenditure> lines = await _aipExpenditures.GetByActivityIdsAsync(
            activities.Select(a => a.Id).ToList(), cancellationToken);
        ILookup<int, AipProcurementItem> itemsByLine = (await _aipExpenditures.GetProcurementItemsByExpenditureIdsAsync(
                lines.Select(l => l.Id).ToList(), cancellationToken))
            .ToLookup(i => i.ExpenditureId);
        Dictionary<int, Account> accountsById = (await _accountRepo.GetAllAsync(cancellationToken))
            .ToDictionary(a => a.Id);

        Dictionary<int, AipActivity> activityById = activities.ToDictionary(a => a.Id);
        Dictionary<int, List<ReportLine>> linesByAipActivityId = lines
            .GroupBy(l => l.ActivityId)
            .ToDictionary(
                g => g.Key,
                g => g.SelectMany(l => ToLines(l, activityById[l.ActivityId], itemsByLine[l.Id], accountsById)).ToList());

        return ServiceResult<WfpReportDto>.Ok(new WfpReportDto(
            fiscalYear, office.OfficeCode, office.OfficeName, WfpReserveRule.Rate,
            BuildFundSourceReports(programs, projects, activities, linesByAipActivityId, sectorLabelByAipOfficeId),
            WfpReportSources.Aip));
    }

    /// <summary>
    /// Decision 6, as <see cref="InitialWfpReadRule"/> (Permission_Matrix.md §3.4).
    /// <c>CanAccessBudgetPlanning</c> is also the endpoint's gate; it is asked again here so the
    /// service is safe on its own.
    /// </summary>
    private async Task<bool> MayReadInitialReportAsync(User caller, int officeId, CancellationToken ct)
        => InitialWfpReadRule.Permits(caller, officeId,
            await _permissions.CanAccessWfpAsync(caller, ct),
            await _permissions.CanAccessBudgetPlanningAsync(caller, ct));

    /// <summary>The division filter to apply, or null when the caller sees nothing (404).</summary>
    private sealed record InitialDivision(int? DivisionId);

    /// <summary>
    /// The caller's OWN office follows the WFP report's division rule (RAL-136):
    /// <list type="bullet">
    ///   <item><description>Host office: <see cref="WfpDivisionScope"/>, exactly as the entered WFP report — finance and Admin choose, Staff are forced to their division, Staff with none see nothing.</description></item>
    ///   <item><description>Guest office: Admin and Staff with no division choose (they read the whole office's AIP in AIP Entry); Staff with a division are forced to it, as <see cref="AipReadScope"/> narrows them.</description></item>
    /// </list>
    /// Another office (a PPDO reader on a guest office) is never narrowed by the reader's own division,
    /// as <see cref="AipReadScope"/> never narrows another office: the requested division is honoured.
    /// </summary>
    private async Task<InitialDivision?> TryResolveInitialDivisionAsync(
        User caller, int officeId, int? requestedDivisionId, CancellationToken ct)
    {
        if (caller.OfficeId != officeId) return new InitialDivision(requestedDivisionId);

        if (OfficeScope.IsHostOfficeUser(caller))
            return WfpDivisionScope.TryFilter(await WfpDivisionScope.ResolveAsync(caller, _permissions), requestedDivisionId,
                out int? hostDivision)
                ? new InitialDivision(hostDivision)
                : null;

        bool forced = caller.Role is not (UserRole.SuperAdmin or UserRole.Admin) && caller.DivisionId is not null;
        return new InitialDivision(forced ? caller.DivisionId : requestedDivisionId);
    }

    /// <summary>
    /// One AIP line as report rows: one per non-zero column (PS / MOOE / CO), so the expense class is
    /// the column the AIP put the money in. Normally that is one row; an older line with money in two
    /// columns gives two.
    /// <list type="bullet">
    ///   <item><description>Net = the AIP's base figure (decision 8, no +30% uplift); Q1–Q4 by <see cref="InitialWfpQuarters"/>, summing to it exactly.</description></item>
    ///   <item><description>Reserve = <see cref="WfpReserveRule"/> on MOOE and CO only, never PS (decisions 3, 7); Total = Net + Reserve, as in the WFP.</description></item>
    ///   <item><description>Items go to the row of the line's account class (or its largest column when the account is missing or matches none).</description></item>
    ///   <item><description>Nature: Procurement when the items account for the row, Combined when they cover part of it, otherwise the account's default nature, else Non-Procurement.</description></item>
    /// </list>
    /// </summary>
    private static IEnumerable<ReportLine> ToLines(
        AipExpenditure line, AipActivity activity, IEnumerable<AipProcurementItem> lineItems,
        IReadOnlyDictionary<int, Account> accountsById)
    {
        List<(string Class, decimal Amount)> columns = new List<(string, decimal)>
            { ("PS", line.Ps), ("MOOE", line.Mooe), ("CO", line.Co) }
            .Where(c => c.Item2 != 0m)
            .ToList();
        if (columns.Count == 0) yield break;

        Account? account = line.AccountId is int accountId ? accountsById.GetValueOrDefault(accountId) : null;
        string itemClass = columns.Any(c => c.Class == account?.ExpenseClass)
            ? account!.ExpenseClass
            : columns.MaxBy(c => c.Amount).Class;
        (int start, int end) = InitialWfpQuarters.ActivityMonths(activity.StartDate, activity.EndDate);
        string fund = (string.IsNullOrWhiteSpace(line.FundingSourceNameSnapshot)
            ? DefaultFundSourceName
            : line.FundingSourceNameSnapshot).ToUpperInvariant();

        foreach ((string expenseClass, decimal net) in columns)
        {
            List<(int PeriodNo, decimal Amount)> items = expenseClass == itemClass
                ? lineItems.Select(i => (i.PeriodNo, i.LineTotal)).ToList()
                : [];
            decimal[] q = InitialWfpQuarters.ForLine(net, items, start, end);
            decimal reserve = expenseClass is "MOOE" or "CO" ? WfpReserveRule.Cap(net) : 0m;
            decimal itemised = items.Sum(i => i.Amount);
            string nature = items.Count == 0
                ? account?.DefaultNature ?? WfpNature.NonProcurement
                : itemised == net ? WfpNature.Procurement : WfpNature.Combined;

            yield return new ReportLine(fund, expenseClass, nature, line.AccountNumberSnapshot, line.AccountTitleSnapshot,
                new WfpReportAmountsDto(net + reserve, reserve, net, q[0], q[1], q[2], q[3], net));
        }
    }
}
