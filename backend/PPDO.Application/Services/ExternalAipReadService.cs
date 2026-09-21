using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IExternalAipReadService"/> (v1.8.0 — PPDO-14).</summary>
public sealed class ExternalAipReadService : IExternalAipReadService
{
    private readonly IAipRepository _aip;
    private readonly IAipExpenditureRepository _expenditures;
    private readonly IAuditRepository _audit;
    private readonly IOfficeRepository _offices;
    private readonly IRepository<FundingSource> _fundingSources;
    private readonly IPriceIndexItemRepository _priceIndexItems;

    public ExternalAipReadService(
        IAipRepository aip,
        IAipExpenditureRepository expenditures,
        IAuditRepository audit,
        IOfficeRepository offices,
        IRepository<FundingSource> fundingSources,
        IPriceIndexItemRepository priceIndexItems)
    {
        _aip             = aip;
        _expenditures    = expenditures;
        _audit           = audit;
        _offices         = offices;
        _fundingSources  = fundingSources;
        _priceIndexItems = priceIndexItems;
    }

    /// <inheritdoc />
    public async Task<ExternalAipDto?> GetAsync(
        int fiscalYear, Office? filterOffice, CancellationToken ct = default)
    {
        ReleaseState? state = await DetermineReleaseStateAsync(fiscalYear, filterOffice?.Id, ct);
        if (state is null) return null;

        IReadOnlyList<Office> allOffices = await _offices.GetAllAsync(ct);
        Dictionary<int, Office> officesById = allOffices.ToDictionary(o => o.Id);

        // ── Everything below released offices, loaded in a handful of set-based queries —
        // never a loop per office (build spec §2 decision 13). ──────────────────────────────

        List<AipOffice> releasedGroups = state.GroupsByOffice
            .Where(kv => state.ReleasedOfficeIds.Contains(kv.Key))
            .SelectMany(kv => kv.Value)
            .ToList();
        List<int> releasedGroupIds = releasedGroups.Select(g => g.Id).ToList();

        IReadOnlyList<AipProgram> programs = await _aip.GetProgramsByOfficeIdsAsync(releasedGroupIds, ct);
        IReadOnlyList<AipProject> projects =
            await _aip.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList(), ct);
        IReadOnlyList<AipActivity> activities =
            await _aip.GetActivitiesByProjectIdsAsync(projects.Select(p => p.Id).ToList(), ct);

        bool isFy2028 = state.AipFormat == ExternalAipConstants.FormatFy2028;

        IReadOnlyList<AipExpenditure> expenditureLines = isFy2028
            ? await _expenditures.GetByActivityIdsAsync(activities.Select(a => a.Id).ToList(), ct)
            : [];
        IReadOnlyList<AipProcurementItem> procurementItems = isFy2028
            ? await _expenditures.GetProcurementItemsByExpenditureIdsAsync(
                expenditureLines.Select(e => e.Id).ToList(), ct)
            : [];

        List<int> priceIndexItemIds = procurementItems
            .Where(i => i.PriceIndexItemId.HasValue)
            .Select(i => i.PriceIndexItemId!.Value)
            .Distinct()
            .ToList();
        IReadOnlyDictionary<int, string?> stockCardNoByPriceIndexItemId = priceIndexItemIds.Count == 0
            ? new Dictionary<int, string?>()
            : (await _priceIndexItems.GetByIdsAsync(priceIndexItemIds, ct))
                .ToDictionary(p => p.Id, p => p.StockCardNo);

        IReadOnlyList<FundingSource> fundingSourceRows = await _fundingSources.GetAllAsync(ct);
        Dictionary<int, FundingSource> fundingSourcesById = fundingSourceRows.ToDictionary(f => f.Id);

        // releasedAt: one batched audit read across every released office's group ids, grouped in
        // memory to a per-office latest — a single query regardless of how many offices this is.
        Dictionary<int, DateTime> releasedAtByOfficeId = [];
        if (isFy2028 && releasedGroupIds.Count > 0)
        {
            IReadOnlyList<AuditLog> acceptRows = await _audit.GetByRecordIdsAsync(
                "aip_offices", releasedGroupIds, [AuditAction.AcceptByPpdo], ct);
            Dictionary<int, int> groupIdToOfficeId =
                releasedGroups.ToDictionary(g => g.Id, g => g.OfficeId!.Value);

            releasedAtByOfficeId = acceptRows
                .Where(a => a.RecordId is int rid && groupIdToOfficeId.ContainsKey(rid))
                .GroupBy(a => groupIdToOfficeId[a.RecordId!.Value])
                .ToDictionary(g => g.Key, g => g.Max(a => a.ChangedAt));
        }

        ILookup<int, AipProgram> programsByGroupId = programs.ToLookup(p => p.OfficeId);
        ILookup<int, AipProject> projectsByProgramId = projects.ToLookup(p => p.ProgramId);
        ILookup<int, AipActivity> activitiesByProjectId = activities.ToLookup(a => a.ProjectId);
        ILookup<int, AipExpenditure> expendituresByActivityId = expenditureLines.ToLookup(e => e.ActivityId);
        ILookup<int, AipProcurementItem> itemsByExpenditureId = procurementItems.ToLookup(i => i.ExpenditureId);

        // Built alongside the DTO so the AIP-document-order sort below never has to re-derive an
        // office id or a total from already-formatted strings.
        List<(string MinGroupRefCode, decimal Ps, decimal Mooe, decimal Co, AipPrintedAmountsDto Printed, ExternalOfficeAipDto Dto)> officeRows = [];

        foreach (var (officeId, groups) in state.GroupsByOffice.Where(kv => state.ReleasedOfficeIds.Contains(kv.Key)))
        {
            if (!officesById.TryGetValue(officeId, out Office? office)) continue; // defensive — config office was deleted; never happens (offices are soft-deleted only)

            List<ExternalGroupDto> groupDtos = [];
            decimal officePs = 0m, officeMooe = 0m, officeCo = 0m;
            List<AipPrintedAmountsDto> officePrintedParts = [];

            foreach (AipOffice group in groups.OrderBy(g => g.RefCode, StringComparer.Ordinal))
            {
                decimal groupPs = 0m, groupMooe = 0m, groupCo = 0m;
                List<AipPrintedAmountsDto> groupPrintedParts = [];

                List<ExternalProgramDto> programDtos = [];
                foreach (AipProgram program in programsByGroupId[group.Id].OrderBy(p => p.RefCode, StringComparer.Ordinal))
                {
                    List<ExternalProjectDto> projectDtos = [];
                    List<AipPrintedAmountsDto> programPrintedParts = [];
                    decimal programPs = 0m, programMooe = 0m, programCo = 0m;

                    foreach (AipProject project in projectsByProgramId[program.Id].OrderBy(p => p.RefCode, StringComparer.Ordinal))
                    {
                        List<ExternalActivityDto> projectActivities = [];
                        List<AipPrintedAmountsDto> projectPrintedParts = [];
                        decimal projectPs = 0m, projectMooe = 0m, projectCo = 0m;

                        foreach (AipActivity activity in activitiesByProjectId[project.Id].OrderBy(a => a.RefCode, StringComparer.Ordinal))
                        {
                            IReadOnlyList<AipExpenditure> ownLines = isFy2028
                                ? (IReadOnlyList<AipExpenditure>)expendituresByActivityId[activity.Id].ToList()
                                : [];

                            ExternalActivityDto dto = ExternalAipMapper.MapActivity(
                                activity, ownLines,
                                ownLines.ToDictionary(
                                    e => e.Id, e => (IReadOnlyList<AipProcurementItem>)itemsByExpenditureId[e.Id].ToList()),
                                stockCardNoByPriceIndexItemId, fundingSourcesById, state.AipFormat, fiscalYear);

                            projectActivities.Add(dto);

                            // Same figures the mapper just formatted into `dto.Amounts` — computed
                            // here from source rather than parsed back out of that string, so the
                            // running total is never a round trip through text.
                            (decimal activityPs, decimal activityMooe, decimal activityCo) = isFy2028
                                ? (ownLines.Sum(e => e.Ps), ownLines.Sum(e => e.Mooe), ownLines.Sum(e => e.Co))
                                : (activity.Ps ?? 0m, activity.Mooe ?? 0m, activity.Co ?? 0m);
                            projectPs += activityPs; projectMooe += activityMooe; projectCo += activityCo;

                            if (isFy2028)
                                projectPrintedParts.Add(AipPrintedFigures.ForActivity(activity, fiscalYear));
                        }

                        AipPrintedAmountsDto projectPrintedTotal = AipPrintedFigures.Sum(projectPrintedParts);

                        projectDtos.Add(new ExternalProjectDto(
                            project.RefCode,
                            project.Name,
                            project.IsSynthetic,
                            ExternalAipMapper.Amounts(projectPs, projectMooe, projectCo),
                            isFy2028 ? ExternalAipMapper.PrintedAmounts(projectPrintedTotal) : null,
                            projectActivities));

                        programPrintedParts.AddRange(projectPrintedParts);
                        programPs += projectPs; programMooe += projectMooe; programCo += projectCo;
                    }

                    AipPrintedAmountsDto programPrintedTotal = AipPrintedFigures.Sum(programPrintedParts);

                    programDtos.Add(new ExternalProgramDto(
                        program.RefCode,
                        program.Name,
                        program.FunctionBand,
                        ExternalAipMapper.Amounts(programPs, programMooe, programCo),
                        isFy2028 ? ExternalAipMapper.PrintedAmounts(programPrintedTotal) : null,
                        projectDtos));

                    groupPrintedParts.AddRange(programPrintedParts);
                    groupPs += programPs; groupMooe += programMooe; groupCo += programCo;
                }

                AipPrintedAmountsDto groupPrintedTotal = AipPrintedFigures.Sum(groupPrintedParts);
                groupDtos.Add(new ExternalGroupDto(
                    ExternalAipMapper.MapSector(group.Sector),
                    group.RefCode, group.Name,
                    ExternalAipMapper.Amounts(groupPs, groupMooe, groupCo),
                    isFy2028 ? ExternalAipMapper.PrintedAmounts(groupPrintedTotal) : null,
                    programDtos));

                officePs += groupPs; officeMooe += groupMooe; officeCo += groupCo;
                if (isFy2028) officePrintedParts.Add(groupPrintedTotal);
            }

            AipPrintedAmountsDto officePrintedTotal = AipPrintedFigures.Sum(officePrintedParts);

            IReadOnlyList<ExternalFundTotalDto> totalsByFund = BuildOfficeFundTotals(
                groups, activitiesByProjectId, projectsByProgramId, programsByGroupId,
                expendituresByActivityId, fundingSourcesById, isFy2028);

            ExternalOfficeAipDto officeDto = new(
                new ExternalOfficeRefDto(office.OfficeCode, office.OfficeName),
                isFy2028 ? ExternalAipConstants.StatusConsolidated : ExternalAipConstants.StatusFinal,
                releasedAtByOfficeId.TryGetValue(officeId, out DateTime releasedAtUtc) ? ToManilaIso(releasedAtUtc) : null,
                ExternalAipMapper.Amounts(officePs, officeMooe, officeCo),
                isFy2028 ? ExternalAipMapper.PrintedAmounts(officePrintedTotal) : null,
                totalsByFund,
                groupDtos);

            string minGroupRefCode = groups.Min(g => g.RefCode)!; // groups is never empty — it came from a real GroupBy
            officeRows.Add((minGroupRefCode, officePs, officeMooe, officeCo, officePrintedTotal, officeDto));
        }

        // AIP document order: by each office's first (lowest) group ref code.
        officeRows = officeRows.OrderBy(r => r.MinGroupRefCode, StringComparer.Ordinal).ToList();
        List<ExternalOfficeAipDto> officeDtos = officeRows.Select(r => r.Dto).ToList();

        List<ExternalOfficeRefDto> pendingDtos = state.PendingOfficeIds
            .Select(id => officesById.TryGetValue(id, out Office? o) ? o : null)
            .Where(o => o is not null)
            .Select(o => new ExternalOfficeRefDto(o!.OfficeCode, o.OfficeName))
            .OrderBy(o => o.Code, StringComparer.Ordinal)
            .ToList();

        decimal totalPs = officeRows.Sum(r => r.Ps);
        decimal totalMooe = officeRows.Sum(r => r.Mooe);
        decimal totalCo = officeRows.Sum(r => r.Co);
        AipPrintedAmountsDto topPrinted = AipPrintedFigures.Sum(officeRows.Select(r => r.Printed));

        return new ExternalAipDto(
            ExternalAipConstants.SchemaVersion,
            ToManilaIso(DateTime.UtcNow),
            fiscalYear,
            state.AipFormat,
            ExternalAipConstants.Currency,
            filterOffice?.OfficeCode,
            ExternalAipMapper.Amounts(totalPs, totalMooe, totalCo),
            isFy2028 ? ExternalAipMapper.PrintedAmounts(topPrinted) : null,
            SumFundTotals(officeDtos.SelectMany(o => o.TotalsByFundingSource)),
            officeDtos,
            pendingDtos);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetFiscalYearsAsync(Office? filterOffice, CancellationToken ct = default)
    {
        IReadOnlyList<int> years = await _aip.GetDistinctFiscalYearsAsync(ct);
        List<int> released = [];

        foreach (int year in years)
        {
            ReleaseState? state = await DetermineReleaseStateAsync(year, filterOffice?.Id, ct);
            if (state is { ReleasedOfficeIds.Count: > 0 })
                released.Add(year);
        }

        return released;
    }

    // ── Release determination ─────────────────────────────────────────────────

    private sealed record ReleaseState(
        AipRecord Record,
        string AipFormat,
        IReadOnlyDictionary<int, List<AipOffice>> GroupsByOffice,
        IReadOnlySet<int> ReleasedOfficeIds,
        IReadOnlySet<int> PendingOfficeIds);

    /// <summary>
    /// Loads the year's AipOffice rows and decides which config offices are released and which
    /// are pending — build spec §2 decision 1 (every group Consolidated) and decision 2 (FY2027
    /// and earlier: the whole record's Status == Final gates every linked office at once, with no
    /// pending concept). Returns null when the year has no AIP at all, or a legacy year whose
    /// record is still Draft (build spec §3.1 "Legacy draft").
    /// </summary>
    private async Task<ReleaseState?> DetermineReleaseStateAsync(
        int fiscalYear, int? filterOfficeId, CancellationToken ct)
    {
        AipRecord? record = await _aip.GetLatestByFiscalYearAsync(fiscalYear, ct);
        if (record is null) return null;

        bool isEntered = AipFiscalYears.IsEntered(fiscalYear);
        string format = isEntered ? ExternalAipConstants.FormatFy2028 : ExternalAipConstants.FormatLegacy;

        if (!isEntered && record.Status != PlanningStatus.Final) return null;

        IReadOnlyList<AipOffice> allGroups = await _aip.GetOfficesByAipIdAsync(record.Id, ct);

        // Legacy AIP office rows with no config office link have no code to address them by and
        // are excluded entirely (docs/external-api/README.md §6).
        Dictionary<int, List<AipOffice>> byOffice = allGroups
            .Where(g => g.OfficeId.HasValue)
            .GroupBy(g => g.OfficeId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        if (filterOfficeId is int fid)
        {
            byOffice = byOffice.TryGetValue(fid, out List<AipOffice>? mine)
                ? new Dictionary<int, List<AipOffice>> { [fid] = mine }
                : [];
        }

        HashSet<int> released = [];
        HashSet<int> pending = [];

        foreach ((int officeId, List<AipOffice> groups) in byOffice)
        {
            // Legacy: gated once already, above, at the whole-record level — every linked office
            // is released together, and the format carries no pending concept at all.
            // FY2028+: released only when every one of the office's groups is Consolidated —
            // never a partial office (build spec §2 decision 1 / §3.1 "One office, partly accepted").
            bool isReleased = !isEntered || groups.All(g => g.WorkflowStatus == AipWorkflowStatus.Consolidated);

            if (isReleased) released.Add(officeId);
            else pending.Add(officeId);
        }

        return new ReleaseState(record, format, byOffice, released, pending);
    }

    // ── Totals by funding source ──────────────────────────────────────────────

    private static IReadOnlyList<ExternalFundTotalDto> BuildOfficeFundTotals(
        List<AipOffice> groups,
        ILookup<int, AipActivity> activitiesByProjectId,
        ILookup<int, AipProject> projectsByProgramId,
        ILookup<int, AipProgram> programsByGroupId,
        ILookup<int, AipExpenditure> expendituresByActivityId,
        IReadOnlyDictionary<int, FundingSource> fundingSourcesById,
        bool isFy2028)
    {
        IEnumerable<AipActivity> officeActivities = groups
            .SelectMany(g => programsByGroupId[g.Id])
            .SelectMany(p => projectsByProgramId[p.Id])
            .SelectMany(j => activitiesByProjectId[j.Id]);

        if (isFy2028)
        {
            return officeActivities
                .SelectMany(a => expendituresByActivityId[a.Id])
                .GroupBy(e => ExternalAipMapper.MapLineFundingSource(e))
                .Select(g => new ExternalFundTotalDto(
                    g.Key, ExternalAipMapper.Amounts(g.Sum(e => e.Ps), g.Sum(e => e.Mooe), g.Sum(e => e.Co))))
                .OrderBy(f => f.FundingSource.Code, StringComparer.Ordinal)
                .ToList();
        }

        return officeActivities
            .Where(a => !string.IsNullOrWhiteSpace(a.FundingSourceSnapshot))
            .GroupBy(a => ExternalAipMapper.MapLegacyFundingSource(
                a.FundingSourceSnapshot, a.FundingSourceId, fundingSourcesById)!)
            .Select(g => new ExternalFundTotalDto(
                g.Key,
                ExternalAipMapper.Amounts(
                    g.Sum(a => a.Ps ?? 0m), g.Sum(a => a.Mooe ?? 0m), g.Sum(a => a.Co ?? 0m))))
            .OrderBy(f => f.FundingSource.Code, StringComparer.Ordinal)
            .ToList();
    }

    private static IReadOnlyList<ExternalFundTotalDto> SumFundTotals(IEnumerable<ExternalFundTotalDto> parts)
        => parts
            .GroupBy(f => f.FundingSource)
            .Select(g => new ExternalFundTotalDto(
                g.Key,
                ExternalAipMapper.Amounts(
                    g.Sum(f => decimal.Parse(f.Amounts.Ps, System.Globalization.CultureInfo.InvariantCulture)),
                    g.Sum(f => decimal.Parse(f.Amounts.Mooe, System.Globalization.CultureInfo.InvariantCulture)),
                    g.Sum(f => decimal.Parse(f.Amounts.Co, System.Globalization.CultureInfo.InvariantCulture)))))
            .OrderBy(f => f.FundingSource.Code, StringComparer.Ordinal)
            .ToList();

    // ── Time ───────────────────────────────────────────────────────────────────

    /// <summary>Manila has no DST — a fixed +08:00 offset conversion needs no TimeZoneInfo lookup.</summary>
    private static string ToManilaIso(DateTime utc)
        => new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc))
            .ToOffset(TimeSpan.FromHours(8))
            .ToString("yyyy-MM-ddTHH:mm:sszzz");
}
