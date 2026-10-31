using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// PPMP Report preview (v1.5 — RAL-183). See <see cref="IPpmpReportService"/> for the contract
/// and <c>docs/v1.5/PPMP_Report_Findings.md</c> §8 for the locked spec.
///
/// Shares the WFP report's data-gathering shape (walk the office's AIP hierarchy, fetch every
/// division's WfpExpenditure rows in a fixed number of batched queries), then projects it
/// <b>item-grained</b> instead of expense-class-grained: procurement items are grouped by
/// identity within their account and spread into the form's four quarter columns. Procurement
/// only (Q8) — expenditures with no procurement items (pure Non-Procurement) are skipped, and a
/// Combined expenditure contributes only its procurement items. Account section totals are the
/// sum of the items below (Q11). One table per fund source (Q4).
///
/// ↩️ <b>PPDO-263 (v1.9.2): an entered year reads the AIP's own procurement items instead</b>
/// (<c>PpmpReportService.Initial.cs</c>). Both sources are reduced to <see cref="SourceLine"/>s, one
/// per expenditure line with its items already placed in a quarter, and share every step after that.
/// </summary>
public sealed partial class PpmpReportService : IPpmpReportService
{
    private const string DefaultFundSourceName = "GENERAL FUND";

    private readonly IAipRepository            _aipRepo;
    private readonly IWfpRepository            _wfpRepo;
    private readonly IWfpExpenditureService    _expenditures;
    private readonly IOfficeRepository       _officeRepo;
    private readonly IDivisionRepository     _divisionRepo;
    private readonly IPriceIndexItemRepository _priceIndexRepo;
    // PPDO-263 — the initial PPMP reads the AIP's lines and items, and checks CanAccessWfp itself.
    private readonly IAipExpenditureRepository _aipExpenditures;
    private readonly IPermissionService        _permissions;

    public PpmpReportService(
        IAipRepository            aipRepo,
        IWfpRepository            wfpRepo,
        IWfpExpenditureService    expenditures,
        IOfficeRepository       officeRepo,
        IDivisionRepository     divisionRepo,
        IPriceIndexItemRepository priceIndexRepo,
        IAipExpenditureRepository aipExpenditures,
        IPermissionService        permissions)
    {
        _aipExpenditures = aipExpenditures;
        _permissions     = permissions;
        _aipRepo        = aipRepo;
        _wfpRepo        = wfpRepo;
        _expenditures   = expenditures;
        _officeRepo     = officeRepo;
        _divisionRepo   = divisionRepo;
        _priceIndexRepo = priceIndexRepo;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PpmpReportDto>> GetReportAsync(
        int officeId, int fiscalYear, int? divisionId = null, CancellationToken cancellationToken = default)
    {
        Office? office = await _officeRepo.GetByIdAsync(officeId, cancellationToken);
        if (office is null)
            return ServiceResult<PpmpReportDto>.NotFound($"Office {officeId} not found.");
        if (string.IsNullOrWhiteSpace(office.OfficeRefCode))
            return ServiceResult<PpmpReportDto>.NotFound($"Office {office.OfficeName} has no AIP reference code configured.");

        AipRecord? aipRecord = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, cancellationToken);
        if (aipRecord is null)
            return ServiceResult<PpmpReportDto>.NotFound($"No AIP found for fiscal year {fiscalYear}.");

        List<AipOffice> aipOffices = (await _aipRepo.GetOfficesByAipIdNoTrackingAsync(aipRecord.Id, cancellationToken))
            .Where(o => o.OfficeId == office.Id)
            .ToList();
        if (aipOffices.Count == 0)
            return ServiceResult<PpmpReportDto>.NotFound($"No AIP hierarchy found for {office.OfficeName} under FY {fiscalYear}.");

        List<int> aipOfficeIds = aipOffices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(aipOfficeIds, cancellationToken);
        List<int> programIds = programs.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject> projects = await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(programIds, cancellationToken);
        List<int> projectIds = projects.Select(p => p.Id).ToList();
        IReadOnlyList<AipActivity> activities = await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(projectIds, cancellationToken);

        // AipActivityId -> every wfp_activity row (all divisions when divisionId is null; one
        // division when provided — RAL-136), then every expenditure, in batched queries.
        Dictionary<int, List<int>> wfpActivityIdsByAipActivityId =
            await BuildWfpActivityMapAsync(aipRecord.Id, officeId, divisionId, cancellationToken);

        List<int> allWfpActivityIds = wfpActivityIdsByAipActivityId.Values.SelectMany(ids => ids).ToList();
        IReadOnlyDictionary<int, IReadOnlyList<WfpExpenditureDto>> expendituresByWfpActivityId =
            await _expenditures.GetByActivityIdsAsync(allWfpActivityIds, cancellationToken);

        Dictionary<int, List<SourceLine>> linesByAipActivityId = [];
        foreach (AipActivity activity in activities)
        {
            List<SourceLine> merged = [];
            foreach (int wfpActivityId in wfpActivityIdsByAipActivityId.GetValueOrDefault(activity.Id, []))
                if (expendituresByWfpActivityId.TryGetValue(wfpActivityId, out IReadOnlyList<WfpExpenditureDto>? e))
                    merged.AddRange(e.Where(HasProcurementItems).Select(FromWfp));
            if (merged.Count > 0)
                linesByAipActivityId[activity.Id] = merged;
        }

        return ServiceResult<PpmpReportDto>.Ok(await AssembleAsync(
            fiscalYear, office, divisionId, programs, projects, activities, linesByAipActivityId,
            WfpReportSources.Wfp, cancellationToken));
    }

    /// <summary>
    /// Everything after the source is read, shared by the WFP and AIP sources: the live Price-Index
    /// join, one block per fund source, and the division's name for the header.
    /// </summary>
    private async Task<PpmpReportDto> AssembleAsync(
        int fiscalYear, Office office, int? divisionId,
        IReadOnlyList<AipProgram> programs, IReadOnlyList<AipProject> projects, IReadOnlyList<AipActivity> activities,
        IReadOnlyDictionary<int, List<SourceLine>> linesByAipActivityId, string source, CancellationToken ct)
    {
        // Live Price-Index join for Stock Card No. + Category (Q15) — one query for every distinct
        // PriceIndexItemId referenced by any procurement item; free-typed items (null id) stay blank.
        IReadOnlyDictionary<int, (string? StockCardNo, string? Category)> priceLookup =
            await BuildPriceLookupAsync(linesByAipActivityId.Values, ct);

        List<string> fundSourceNames = linesByAipActivityId.Values
            .SelectMany(x => x)
            .Where(l => l.Items.Count > 0)
            .Select(l => l.Fund)
            .Distinct()
            .OrderBy(n => n.Equals(DefaultFundSourceName, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<PpmpReportFundSourceDto> fundSourceReports = fundSourceNames
            .Select(fundSourceName => BuildFundSource(
                fundSourceName, programs, projects, activities, linesByAipActivityId, priceLookup))
            .Where(f => f.Programs.Count > 0)
            .ToList();

        string? divisionName = null;
        if (divisionId is int did)
            divisionName = (await _divisionRepo.GetByIntIdAsync(did, ct))?.Name;

        return new PpmpReportDto(
            fiscalYear, office.OfficeCode, office.OfficeName, divisionName, fundSourceReports, source);
    }

    // ── Data gathering ───────────────────────────────────────────────────────

    private async Task<Dictionary<int, List<int>>> BuildWfpActivityMapAsync(
        int aipRecordId, int officeId, int? divisionId, CancellationToken ct)
    {
        IReadOnlyList<WfpRecord> wfpRecords = await _wfpRepo.GetFilteredAsync(aipRecordId, officeId, divisionId, ct);
        if (wfpRecords.Count == 0) return [];

        IReadOnlyList<WfpActivity> wfpActivities = await _wfpRepo.GetActivitiesByWfpIdsAsync(
            wfpRecords.Select(r => r.Id).ToList(), ct);

        Dictionary<int, List<int>> map = [];
        foreach (WfpActivity wa in wfpActivities)
        {
            if (!map.TryGetValue(wa.AipActivityId, out List<int>? ids))
                map[wa.AipActivityId] = ids = [];
            ids.Add(wa.Id);
        }
        return map;
    }

    private async Task<IReadOnlyDictionary<int, (string? StockCardNo, string? Category)>> BuildPriceLookupAsync(
        IEnumerable<List<SourceLine>> lineLists, CancellationToken ct)
    {
        List<int> ids = lineLists
            .SelectMany(list => list)
            .SelectMany(l => l.Items)
            .Where(i => i.PriceIndexItemId.HasValue)
            .Select(i => i.PriceIndexItemId!.Value)
            .Distinct()
            .ToList();
        if (ids.Count == 0) return new Dictionary<int, (string?, string?)>();

        IReadOnlyList<PriceIndexItem> items = await _priceIndexRepo.GetByIdsAsync(ids, ct);
        return items.ToDictionary(p => p.Id, p => (p.StockCardNo, p.Category));
    }

    // ── Hierarchy building (one fund source) ───────────────────────────────────

    private static PpmpReportFundSourceDto BuildFundSource(
        string fundSourceName,
        IReadOnlyList<AipProgram> programs,
        IReadOnlyList<AipProject> projects,
        IReadOnlyList<AipActivity> activities,
        IReadOnlyDictionary<int, List<SourceLine>> linesByAipActivityId,
        IReadOnlyDictionary<int, (string? StockCardNo, string? Category)> priceLookup)
    {
        // project id -> its activity DTOs (built once, then bucketed under programs)
        Dictionary<int, List<PpmpReportActivityDto>> activityDtosByProjectId = [];
        foreach (AipActivity activity in activities.OrderBy(a => RefCodeSequence(a.RefCode)).ThenBy(a => a.RefCode))
        {
            if (!linesByAipActivityId.TryGetValue(activity.Id, out List<SourceLine>? all))
                continue;

            List<SourceLine> forFund = all
                .Where(l => l.Fund == fundSourceName && l.Items.Count > 0)
                .ToList();
            if (forFund.Count == 0) continue;

            List<PpmpReportAccountDto> accounts = BuildAccounts(forFund, priceLookup);
            if (accounts.Count == 0) continue;

            decimal activityTotal = accounts.Sum(a => a.Total);
            if (!activityDtosByProjectId.TryGetValue(activity.ProjectId, out List<PpmpReportActivityDto>? list))
                activityDtosByProjectId[activity.ProjectId] = list = [];
            list.Add(new PpmpReportActivityDto(activity.RefCode, activity.Name, accounts, activityTotal));
        }

        Dictionary<int, List<PpmpReportProjectDto>> projectDtosByProgramId = [];
        foreach (AipProject project in projects.OrderBy(p => RefCodeSequence(p.RefCode)).ThenBy(p => p.RefCode))
        {
            if (!activityDtosByProjectId.TryGetValue(project.Id, out List<PpmpReportActivityDto>? activityDtos))
                continue;
            decimal projectTotal = activityDtos.Sum(a => a.Total);
            if (!projectDtosByProgramId.TryGetValue(project.ProgramId, out List<PpmpReportProjectDto>? list))
                projectDtosByProgramId[project.ProgramId] = list = [];
            list.Add(new PpmpReportProjectDto(project.RefCode, project.Name, activityDtos, projectTotal));
        }

        List<PpmpReportProgramDto> programDtos = [];
        foreach (AipProgram program in programs.OrderBy(p => RefCodeSequence(p.RefCode)).ThenBy(p => p.RefCode))
        {
            if (!projectDtosByProgramId.TryGetValue(program.Id, out List<PpmpReportProjectDto>? projectDtos))
                continue;
            decimal programTotal = projectDtos.Sum(p => p.Total);
            programDtos.Add(new PpmpReportProgramDto(program.RefCode, program.Name, projectDtos, programTotal));
        }

        decimal fundTotal = programDtos.Sum(p => p.Total);
        return new PpmpReportFundSourceDto(fundSourceName, programDtos, fundTotal);
    }

    /// <summary>
    /// Groups an activity's (single-fund) lines by account, then groups each account's procurement
    /// items by identity — (PriceIndexItemId, Name, Unit, UnitPrice) — adding every source item's
    /// Qty/LineTotal into its quarter. Account total = sum of the items' Est. Budget (Q11).
    /// </summary>
    private static List<PpmpReportAccountDto> BuildAccounts(
        IReadOnlyList<SourceLine> lines,
        IReadOnlyDictionary<int, (string? StockCardNo, string? Category)> priceLookup)
    {
        List<PpmpReportAccountDto> accounts = [];

        IEnumerable<IGrouping<(string? Number, string? Title), SourceLine>> byAccount = lines
            .GroupBy(l => (Number: l.AccountNumber, Title: l.AccountTitle))
            .OrderBy(g => g.Key.Number, StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<(string? Number, string? Title), SourceLine> accountGroup in byAccount)
        {
            Dictionary<ItemKey, ItemAccumulator> byItem = [];
            List<ItemKey> order = []; // preserve first-seen order (entry order), like the reference

            foreach (SourceLine line in accountGroup)
            {
                foreach (SourceItem item in line.Items)
                {
                    ItemKey key = new(item.PriceIndexItemId, item.Name, item.Unit, item.UnitPrice);
                    if (!byItem.TryGetValue(key, out ItemAccumulator? acc))
                    {
                        byItem[key] = acc = new ItemAccumulator();
                        order.Add(key);
                    }
                    acc.Add(item.Quarter, item.Qty, item.LineTotal);
                }
            }

            List<PpmpReportItemDto> items = order.Select(k => byItem[k].ToDto(k, priceLookup)).ToList();
            if (items.Count == 0) continue; // account with no procurement items — skip (proc-only)

            accounts.Add(new PpmpReportAccountDto(
                accountGroup.Key.Number, accountGroup.Key.Title, items, items.Sum(i => i.EstBudget)));
        }

        return accounts;
    }

    // ── Small helpers ──────────────────────────────────────────────────────────

    private static bool HasProcurementItems(WfpExpenditureDto e) => e.ProcurementItems.Count > 0;

    /// <summary>A WFP expenditure as a source line: each item placed by the expenditure's frequency.</summary>
    private static SourceLine FromWfp(WfpExpenditureDto e) => new(
        FundSourceNameFor(e), e.AccountNumberSnapshot, e.AccountTitleSnapshot,
        e.ProcurementItems
            .Select(i => new SourceItem(
                QuarterOf(e.Frequency, i.PeriodNo, e.AnnualQuarterChoice),
                i.PriceIndexItemId, i.Name, i.Unit, i.UnitPrice, i.Qty, i.LineTotal))
            .ToList());

    /// <summary>One expenditure line (WFP or AIP) reduced to what the PPMP reads: fund, account and its items.</summary>
    private sealed record SourceLine(
        string Fund, string? AccountNumber, string? AccountTitle, IReadOnlyList<SourceItem> Items);

    /// <summary>One procurement item, already placed in its quarter (1–4). LineTotal is qty × price × days.</summary>
    private sealed record SourceItem(
        int Quarter, int? PriceIndexItemId, string Name, string Unit, decimal UnitPrice, decimal Qty, decimal LineTotal);

    /// <summary>
    /// Maps a 1-based period number to a quarter (1–4) using the same rules as
    /// <c>WfpExpenditureCalculator</c>: Monthly 1-3→Q1 … 10-12→Q4; Quarterly direct; Bi-annual
    /// 1st half→Q1, 2nd half→Q3; Annual→AnnualQuarterChoice (default Q1). Kept in lockstep with the
    /// calculator so an item's per-quarter split reconciles with the expenditure's own Q1–Q4.
    /// </summary>
    private static int QuarterOf(string frequency, int periodNo, int? annualQuarterChoice) => frequency switch
    {
        WfpFrequency.Monthly   => Math.Clamp((periodNo - 1) / 3 + 1, 1, 4),
        WfpFrequency.Quarterly => Math.Clamp(periodNo, 1, 4),
        WfpFrequency.BiAnnual  => periodNo <= 1 ? 1 : 3,
        WfpFrequency.Annual    => annualQuarterChoice is >= 1 and <= 4 ? annualQuarterChoice.Value : 1,
        _                      => 1,
    };

    private static int RefCodeSequence(string refCode)
    {
        string lastSegment = refCode.Split('-')[^1];
        return int.TryParse(lastSegment, out int seq) ? seq : int.MaxValue;
    }

    private static string FundSourceNameFor(WfpExpenditureDto e) => FundName(e.FundingSourceNameSnapshot);

    /// <summary>The block a line's fund snapshot falls under: blank is GENERAL FUND; upper-cased either way.</summary>
    private static string FundName(string? snapshot) =>
        (string.IsNullOrWhiteSpace(snapshot) ? DefaultFundSourceName : snapshot).ToUpperInvariant();

    /// <summary>Item identity for grouping across periods. PriceIndexItemId distinguishes two items that share a name/unit/price but map to different Price-Index rows (and thus different stock cards).</summary>
    private readonly record struct ItemKey(int? PriceIndexItemId, string Name, string Unit, decimal UnitPrice);

    /// <summary>Accumulates one grouped item's per-quarter qty/amount and year totals across its source period rows.</summary>
    private sealed class ItemAccumulator
    {
        private readonly decimal[] _qty = new decimal[4];
        private readonly decimal[] _amt = new decimal[4];

        public void Add(int quarter, decimal qty, decimal lineTotal)
        {
            int i = quarter - 1;
            _qty[i] += qty;
            _amt[i] += lineTotal;
        }

        public PpmpReportItemDto ToDto(
            ItemKey key, IReadOnlyDictionary<int, (string? StockCardNo, string? Category)> priceLookup)
        {
            (string? stockCardNo, string? category) = key.PriceIndexItemId is int id && priceLookup.TryGetValue(id, out var v)
                ? v
                : (null, null);
            decimal qty = _qty[0] + _qty[1] + _qty[2] + _qty[3];
            decimal est = _amt[0] + _amt[1] + _amt[2] + _amt[3];
            return new PpmpReportItemDto(
                stockCardNo, category, key.Name, key.Unit, key.UnitPrice, qty, est,
                _qty[0], _amt[0], _qty[1], _amt[1], _qty[2], _amt[2], _qty[3], _amt[3]);
        }
    }
}
