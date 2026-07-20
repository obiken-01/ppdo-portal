using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Budget Planning Dashboard data service (RAL-80, RAL-92, RAL-60; PPDO-scoped rework — v1.4.5,
/// RAL-161). GetDashboardAsync is permanently scoped to the PPDO office — Budget Planning is
/// effectively PPDO-only in practice — and every query is pushed to SQL via scoped repository
/// methods; the old fleet-wide "N offices set up" view (AllocationSetupOverviewDto) and its
/// 8 unfiltered full-table scans are gone. GetRecentActivityAsync delegates to
/// IAuditRepository.GetRecentAsync so the DB applies ordering, office filtering, and TAKE — the
/// entire audit_log is never loaded. GetOfficeDashboardAsync composes the office-scoped
/// readiness hub by calling IAllocationService for the allocation-setup panel — it never
/// re-implements those queries.
/// </summary>
public sealed class BudgetPlanningDashboardService : IBudgetPlanningDashboardService
{
    private const string PpdoOfficeCode = "PPDO";

    private readonly ILdipRepository            _ldipRepo;
    private readonly IAipRepository             _aipRepo;
    private readonly IWfpRepository             _wfpRepo;
    private readonly IWfpExpenditureRepository  _wfpExpRepo;
    private readonly IOfficeRepository          _officeRepo;
    private readonly IRepository<Division>      _divisionRepo;
    private readonly IRepository<FundingSource> _fundingSourceRepo;
    private readonly IAuditRepository           _auditRepo;
    private readonly IAllocationService         _allocationService;

    public BudgetPlanningDashboardService(
        ILdipRepository            ldipRepo,
        IAipRepository             aipRepo,
        IWfpRepository             wfpRepo,
        IWfpExpenditureRepository  wfpExpRepo,
        IOfficeRepository          officeRepo,
        IRepository<Division>      divisionRepo,
        IRepository<FundingSource> fundingSourceRepo,
        IAuditRepository           auditRepo,
        IAllocationService         allocationService)
    {
        _ldipRepo          = ldipRepo;
        _aipRepo           = aipRepo;
        _wfpRepo           = wfpRepo;
        _wfpExpRepo        = wfpExpRepo;
        _officeRepo        = officeRepo;
        _divisionRepo      = divisionRepo;
        _fundingSourceRepo = fundingSourceRepo;
        _auditRepo         = auditRepo;
        _allocationService = allocationService;
    }

    /// <inheritdoc />
    public async Task<PpdoDashboardDto> GetDashboardAsync(
        int? fiscalYear, int? divisionId, CancellationToken ct = default)
    {
        Office ppdo = await _officeRepo.GetByCodeAsync(PpdoOfficeCode, ct)
            ?? throw new InvalidOperationException($"Office '{PpdoOfficeCode}' is not seeded.");

        (int resolvedFY, IReadOnlyList<int> availableFiscalYears) = await ResolveFiscalYearsAsync(fiscalYear, ct);

        OfficeLdipSummaryDto ldip = await BuildOfficeLdipSummaryAsync(ppdo.Id, resolvedFY, ct);
        OfficeAipSummaryDto  aip  = await BuildOfficeAipSummaryAsync(ppdo.Id, resolvedFY, ct);

        // Divisions in scope: every active division of PPDO, narrowed to one when the caller
        // (the Functions layer) has already clamped divisionId for a non-finance caller.
        List<Division> divisions = (await _divisionRepo.GetAllAsync(ct))
            .Where(d => d.OfficeId == ppdo.Id && d.IsActive
                     && (divisionId == null || d.Id == divisionId.Value))
            .OrderBy(d => d.Name)
            .ToList();

        // Active funds and their office-wide allocations are fetched once per request and
        // reused by both panels below — GetAllocationsAsync already returns every division's
        // amount for a fund, so calling it once per fund (not once per fund PER division) is
        // enough. This replaced an N+1 that fired ~60 sequential DbCommands for a 5-division,
        // 3-fund office (RAL-166 follow-up — v1.4.5 Live Metrics showed the dashboard
        // dominated by repeated GetAllocationsAsync queries).
        IReadOnlyList<FundingSource> activeFunds = (await _fundingSourceRepo.GetAllAsync(ct))
            .Where(f => f.IsActive)
            .ToList();
        Dictionary<int, IReadOnlyList<DivisionAllocationDto>> allocationsByFund =
            await GetAllocationsByFundAsync(ppdo.Id, resolvedFY, activeFunds, ct);

        IReadOnlyList<DivisionWfpStatusDto> wfpByDivision =
            await BuildWfpByDivisionAsync(ppdo.Id, resolvedFY, divisions, activeFunds, allocationsByFund, ct);
        IReadOnlyList<FundCeilingDto> ceilingByFund =
            await BuildCeilingByFundAsync(
                ppdo.Id, resolvedFY, divisions, activeFunds, allocationsByFund, _allocationService, ct);

        return new PpdoDashboardDto(
            resolvedFY, availableFiscalYears, ppdo.Id, ppdo.OfficeCode, ppdo.OfficeName,
            ldip, aip, wfpByDivision, ceilingByFund);
    }

    /// <inheritdoc />
    public async Task<FiscalYearsDto> GetFiscalYearsAsync(
        int? fiscalYear, CancellationToken cancellationToken = default)
    {
        (int resolvedFY, IReadOnlyList<int> availableFiscalYears) =
            await ResolveFiscalYearsAsync(fiscalYear, cancellationToken);
        return new FiscalYearsDto(resolvedFY, availableFiscalYears);
    }

    private async Task<(int ResolvedFY, IReadOnlyList<int> AvailableFiscalYears)> ResolveFiscalYearsAsync(
        int? fiscalYear, CancellationToken ct)
    {
        IReadOnlyList<int> availableFiscalYears = await _aipRepo.GetDistinctFiscalYearsAsync(ct);
        int resolvedFY = fiscalYear
            ?? (availableFiscalYears.Count > 0 ? availableFiscalYears[0] : DateTime.UtcNow.Year + 1);
        return (resolvedFY, availableFiscalYears);
    }

    private async Task<Dictionary<int, IReadOnlyList<DivisionAllocationDto>>> GetAllocationsByFundAsync(
        int officeId, int fiscalYear, IReadOnlyList<FundingSource> activeFunds, CancellationToken ct)
    {
        // One call for every fund's allocations (RAL-166 follow-up, round 2) — was one
        // GetAllocationsAsync call per fund, each of which itself re-resolves the office's
        // divisions and every funding source from scratch, so N funds meant 3N queries just for
        // this. GetAllocationsForAllFundsAsync (built for the Allocation page's own batch
        // endpoint) already does the resolve-once-query-once version; grouping its flat result
        // by FundingSourceId here is free.
        IReadOnlyList<DivisionAllocationDto> allAllocations =
            await _allocationService.GetAllocationsForAllFundsAsync(officeId, fiscalYear, ct);

        return activeFunds.ToDictionary(
            fund => fund.Id,
            fund => (IReadOnlyList<DivisionAllocationDto>)allAllocations
                .Where(a => a.FundingSourceId == fund.Id)
                .ToList());
    }

    private async Task<IReadOnlyList<DivisionWfpStatusDto>> BuildWfpByDivisionAsync(
        int officeId, int fiscalYear, IReadOnlyList<Division> divisions,
        IReadOnlyList<FundingSource> activeFunds,
        IReadOnlyDictionary<int, IReadOnlyList<DivisionAllocationDto>> allocationsByFund,
        CancellationToken ct)
    {
        AipRecord? primaryAip = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, ct);

        // One representative WfpRecord per division: prefer Final, else most recently updated.
        Dictionary<int, WfpRecord> wfpByDivisionMap = [];
        if (primaryAip is not null)
        {
            IReadOnlyList<WfpRecord> records =
                await _wfpRepo.GetFilteredAsync(primaryAip.Id, officeId, divisionId: null, ct);
            wfpByDivisionMap = records
                .Where(w => w.DivisionId.HasValue)
                .GroupBy(w => w.DivisionId!.Value)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(w => w.Status == PlanningStatus.Final)
                          .ThenByDescending(w => w.UpdatedAt)
                          .First());
        }

        List<DivisionWfpStatusDto> result = [];
        foreach (Division division in divisions)
        {
            // Sequential — DbContext is not thread-safe, never Task.WhenAll these.
            WfpActivityCoverageDto coverage =
                await _wfpExpRepo.GetActivityCoverageAsync(officeId, division.Id, fiscalYear, ct);

            List<DivisionFundAmountDto> allocationByFund = activeFunds
                .Select(fund => new DivisionFundAmountDto(
                    fund.Id, fund.Code, fund.Name,
                    allocationsByFund[fund.Id].FirstOrDefault(a => a.DivisionId == division.Id)?.Amount ?? 0m))
                .Where(f => f.Amount > 0m)
                .ToList();

            wfpByDivisionMap.TryGetValue(division.Id, out WfpRecord? wfp);

            result.Add(new DivisionWfpStatusDto(
                division.Id, division.Code, division.Name,
                wfp?.Status ?? "Not started",
                coverage.ActivitiesWithExpenditures, coverage.TotalActivities,
                allocationByFund.Sum(f => f.Amount),
                allocationByFund));
        }

        return result;
    }

    private static async Task<IReadOnlyList<FundCeilingDto>> BuildCeilingByFundAsync(
        int officeId, int fiscalYear, IReadOnlyList<Division> divisionsInScope,
        IReadOnlyList<FundingSource> activeFunds,
        IReadOnlyDictionary<int, IReadOnlyList<DivisionAllocationDto>> allocationsByFund,
        IAllocationService allocationService, CancellationToken ct)
    {
        IReadOnlyList<BudgetCeilingDto> ceilings = await allocationService.GetCeilingsAsync(officeId, fiscalYear, ct);

        List<FundCeilingDto> result = [];
        foreach (FundingSource fund in activeFunds)
        {
            decimal ceiling = ceilings.FirstOrDefault(c => c.FundingSourceId == fund.Id)?.Amount ?? 0m;

            // All divisions' amounts (not just the clamped set) — "Remaining" is an office-wide
            // fact and must reflect the true unallocated portion regardless of who's viewing.
            IReadOnlyList<DivisionAllocationDto> allocations = allocationsByFund[fund.Id];
            decimal remaining = ceiling - allocations.Sum(a => a.Amount);

            List<FundDivisionShareDto> byDivision = divisionsInScope
                .Select(d => new FundDivisionShareDto(
                    d.Id, d.Code, d.Name,
                    allocations.FirstOrDefault(a => a.DivisionId == d.Id)?.Amount ?? 0m))
                .ToList();

            result.Add(new FundCeilingDto(fund.Id, fund.Code, fund.Name, ceiling, remaining, byDivision));
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(
        int? officeId, CancellationToken cancellationToken = default)
    {
        // GetRecentAsync pushes ORDER BY, WHERE (office filter), and TOP(10) to SQL.
        // Actor name is read from the pre-loaded ChangedBy navigation (one JOIN, no second query).
        IReadOnlyList<AuditLog> audits = await _auditRepo.GetRecentAsync(10, officeId, cancellationToken);

        return audits
            .Select(a => new RecentActivityDto(
                a.Id,
                // EF Core loses DateTimeKind on the SQL Server round-trip (datetime2 columns don't
                // store it), so a.ChangedAt reads back as Kind=Unspecified even though AuditService
                // always writes DateTime.UtcNow. Without re-stamping Utc here, System.Text.Json
                // serializes it without a trailing "Z", and the browser's `new Date(...)` then
                // misparses it as local time instead of UTC — displaying a time 8 hours off Manila.
                DateTime.SpecifyKind(a.ChangedAt, DateTimeKind.Utc),
                a.TableName,
                a.Action,
                a.RecordId,
                a.RecordGuid,
                a.ChangedBy?.FullName ?? "Unknown"))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OfficeDashboardDto> GetOfficeDashboardAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken = default)
    {
        AllocationSetupSummaryDto allocation =
            await BuildAllocationSummaryAsync(officeId, fiscalYear, cancellationToken);
        OfficeLdipSummaryDto ldip =
            await BuildOfficeLdipSummaryAsync(officeId, fiscalYear, cancellationToken);
        OfficeAipSummaryDto aip =
            await BuildOfficeAipSummaryAsync(officeId, fiscalYear, cancellationToken);

        return new OfficeDashboardDto(officeId, fiscalYear, allocation, ldip, aip);
    }

    private async Task<AllocationSetupSummaryDto> BuildAllocationSummaryAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken)
    {
        // Readiness summary tracks General Fund only (v1.4.3 — RAL-154) — matches the WFP
        // setup gate, which is likewise GF-only; other funds are optional and not part of
        // "is this office ready for WFP entry".
        int? gfId = await _allocationService.GetGeneralFundIdAsync(cancellationToken);

        decimal? ceilingAmount = null;
        decimal allocated = 0m;

        if (gfId is int generalFundId)
        {
            ServiceResult<BudgetCeilingDto> ceilingResult =
                await _allocationService.GetCeilingAsync(officeId, fiscalYear, generalFundId, cancellationToken);
            ceilingAmount = ceilingResult.IsSuccess ? ceilingResult.Value!.Amount : null;

            IReadOnlyList<DivisionAllocationDto> allocations =
                await _allocationService.GetAllocationsAsync(officeId, fiscalYear, generalFundId, cancellationToken);
            allocated = allocations.Sum(a => a.Amount);
        }

        decimal? remaining = ceilingAmount.HasValue ? ceilingAmount.Value - allocated : null;
        bool isOverAllocated = ceilingAmount.HasValue && allocated > ceilingAmount.Value;

        IReadOnlyList<ProgramAssignmentDto> programs =
            await _allocationService.GetProgramAssignmentsAsync(officeId, fiscalYear, cancellationToken);
        int assignedCount = programs.Count(p => p.DivisionIds.Count > 0);
        int unassignedCount = programs.Count - assignedCount;

        return new AllocationSetupSummaryDto(
            ceilingAmount, allocated, remaining, isOverAllocated, assignedCount, unassignedCount);
    }

    /// <summary>
    /// Office-scoped LDIP summary (un-stubbed by RAL-61, which added
    /// ldip_records.office_id): documents belonging to the office whose year range
    /// covers the selected fiscal year, with a status breakdown.
    /// </summary>
    private async Task<OfficeLdipSummaryDto> BuildOfficeLdipSummaryAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken)
    {
        IReadOnlyList<LdipRecord> records =
            await _ldipRepo.GetListAsync(officeId, null, cancellationToken);
        List<LdipRecord> covering = records
            .Where(r => r.FiscalYearStart <= fiscalYear && fiscalYear <= r.FiscalYearEnd)
            .ToList();
        List<StatusBreakdownDto> breakdown = covering
            .GroupBy(r => r.Status)
            .Select(g => new StatusBreakdownDto(g.Key, g.Count()))
            .ToList();
        return new OfficeLdipSummaryDto(true, covering.Count, breakdown);
    }

    private async Task<OfficeAipSummaryDto> BuildOfficeAipSummaryAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken)
    {
        Office? office = await _officeRepo.GetByIdAsync(officeId, cancellationToken);
        if (office?.OfficeRefCode is null)
            return new OfficeAipSummaryDto(false, null, 0, 0, 0);

        AipRecord? aipRecord = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, cancellationToken);
        if (aipRecord is null)
            return new OfficeAipSummaryDto(false, null, 0, 0, 0);

        IReadOnlyList<AipOffice> aipOffices =
            await _aipRepo.GetOfficesByAipIdAsync(aipRecord.Id, cancellationToken);
        List<AipOffice> matched = aipOffices
            .Where(o => o.RefCode.EndsWith(office.OfficeRefCode, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matched.Count == 0)
            return new OfficeAipSummaryDto(false, aipRecord.Status, 0, 0, 0);

        List<int> officeIds = matched.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsAsync(officeIds, cancellationToken);
        List<int> programIds = programs.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsAsync(programIds, cancellationToken);
        List<int> projectIds = projects.Select(p => p.Id).ToList();
        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(projectIds, cancellationToken);

        return new OfficeAipSummaryDto(
            true, aipRecord.Status, programs.Count, projects.Count, activities.Count);
    }
}
