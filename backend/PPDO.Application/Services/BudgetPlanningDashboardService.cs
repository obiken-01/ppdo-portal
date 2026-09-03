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
/// entire audit_log is never loaded, and results are scoped to Budget Planning's own tables
/// (BudgetPlanningTableNames) so unrelated activity (User Management, Config) doesn't show up
/// here — see the dedicated Audit Log page (RAL-174) for the unfiltered, all-tables view.
/// GetOfficeDashboardAsync composes the office-scoped
/// readiness hub by calling IAllocationService for the allocation-setup panel — it never
/// re-implements those queries.
/// </summary>
public sealed class BudgetPlanningDashboardService : IBudgetPlanningDashboardService
{
    /// <summary>Scopes GetRecentActivityAsync to Budget Planning's own tables (AIP, LDIP, WFP,
    /// Allocation) — excludes "users" and Config tables (accounts, divisions, offices, etc.),
    /// which surface instead on the dedicated Audit Log page (SuperAdmin-only, RAL-174).</summary>
    private static readonly string[] BudgetPlanningTableNames =
    [
        "aip_records", "aip_programs", "aip_activities",
        "ldip_records",
        "wfp_records", "wfp_expenditures",
        "budget_ceilings", "division_allocations", "program_divisions",
    ];

    private readonly ILdipRepository                _ldipRepo;
    private readonly IAipRepository                 _aipRepo;
    private readonly IWfpRepository                 _wfpRepo;
    private readonly IWfpExpenditureRepository      _wfpExpRepo;
    private readonly IWfpAllocationLedgerRepository _ledgerRepo;
    private readonly IOfficeRepository              _officeRepo;
    private readonly IRepository<Division>          _divisionRepo;
    private readonly IRepository<FundingSource>     _fundingSourceRepo;
    private readonly IAuditRepository               _auditRepo;
    private readonly IAllocationService             _allocationService;
    private readonly IBudgetCeilingRepository       _ceilingRepo;
    private readonly IUserRepository                _userRepo;
    private readonly IPermissionService             _permissions;

    public BudgetPlanningDashboardService(
        ILdipRepository                ldipRepo,
        IAipRepository                 aipRepo,
        IWfpRepository                 wfpRepo,
        IWfpExpenditureRepository      wfpExpRepo,
        IWfpAllocationLedgerRepository ledgerRepo,
        IOfficeRepository              officeRepo,
        IRepository<Division>          divisionRepo,
        IRepository<FundingSource>     fundingSourceRepo,
        IAuditRepository               auditRepo,
        IAllocationService             allocationService,
        IBudgetCeilingRepository       ceilingRepo,
        IUserRepository                userRepo,
        IPermissionService             permissions)
    {
        _ldipRepo          = ldipRepo;
        _aipRepo           = aipRepo;
        _wfpRepo           = wfpRepo;
        _wfpExpRepo        = wfpExpRepo;
        _ledgerRepo        = ledgerRepo;
        _officeRepo        = officeRepo;
        _divisionRepo      = divisionRepo;
        _fundingSourceRepo = fundingSourceRepo;
        _auditRepo         = auditRepo;
        _allocationService = allocationService;
        _ceilingRepo       = ceilingRepo;
        _userRepo          = userRepo;
        _permissions       = permissions;
    }

    /// <inheritdoc />
    public async Task<PpdoDashboardDto> GetDashboardAsync(
        int? fiscalYear, int? divisionId, CancellationToken ct = default)
    {
        Office host = await _officeRepo.GetHostOfficeAsync(ct)
            ?? throw new InvalidOperationException(
                "No office is flagged as the host office (offices.is_host_office).");

        (int resolvedFY, IReadOnlyList<int> availableFiscalYears) = await ResolveFiscalYearsAsync(fiscalYear, ct);

        OfficeLdipSummaryDto ldip = await BuildOfficeLdipSummaryAsync(host.Id, resolvedFY, ct);
        OfficeAipSummaryDto  aip  = await BuildOfficeAipSummaryAsync(host.Id, resolvedFY, ct);

        // Divisions in scope: every active division of PPDO, narrowed to one when the caller
        // (the Functions layer) has already clamped divisionId for a non-finance caller.
        List<Division> divisions = (await _divisionRepo.GetAllAsync(ct))
            .Where(d => d.OfficeId == host.Id && d.IsActive
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
            await GetAllocationsByFundAsync(host.Id, resolvedFY, activeFunds, ct);

        IReadOnlyList<DivisionSummaryDto> byDivision =
            await BuildByDivisionAsync(host, resolvedFY, divisions, activeFunds, allocationsByFund, ct);
        IReadOnlyList<FundCeilingDto> ceilingByFund =
            await BuildCeilingByFundAsync(
                host.Id, resolvedFY, divisions, activeFunds, allocationsByFund, _allocationService, ct);

        return new PpdoDashboardDto(
            resolvedFY, availableFiscalYears, host.Id, host.OfficeCode, host.OfficeName,
            ldip, aip, byDivision, ceilingByFund);
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

    /// <summary>
    /// One row per in-scope division: its allocation, what it has costed in the AIP, and its AIP
    /// stage (PPDO-20 — replaces BuildWfpByDivisionAsync).
    ///
    /// <b>How a division is linked to AIP money.</b> There is no division column on the AIP
    /// hierarchy. The link is <c>program_divisions</c> — the PPA assignment — which is keyed by
    /// program REF CODE and is deliberately permanent rather than per fiscal year (see
    /// <see cref="ProgramDivision"/> for why an FK to aip_programs.Id would be wrong). So: resolve
    /// the FY's AIP record, take the host office's AipOffice rows, roll every program up in SQL,
    /// then attribute each program's rollup to whichever divisions its ref code is assigned to.
    ///
    /// ⚠️ <b>A program assigned to two divisions counts in full against both.</b> The column
    /// answers "what is this division responsible for", not "how does the office total split", so
    /// summing it down the page can exceed the office's own figure when a PPA is shared. Splitting
    /// the cost evenly instead would invent a number nobody entered. If shared assignments become
    /// common this needs a real answer — flagged as an open item on the PPDO-20 spec, not decided
    /// here by accident.
    /// </summary>
    private async Task<IReadOnlyList<DivisionSummaryDto>> BuildByDivisionAsync(
        Office host, int fiscalYear, IReadOnlyList<Division> divisions,
        IReadOnlyList<FundingSource> activeFunds,
        IReadOnlyDictionary<int, IReadOnlyList<DivisionAllocationDto>> allocationsByFund,
        CancellationToken ct)
    {
        // Sequential throughout — DbContext is not thread-safe, and Task.WhenAll over two repo
        // calls sharing it is what produced the GetStatsAsync production 500.
        AipRecord? primaryAip = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, ct);

        Dictionary<int, (int Costed, int Total, decimal Amount)> aipByDivision = [];

        if (primaryAip is not null && host.OfficeRefCode is not null)
        {
            IReadOnlyList<AipOffice> aipOffices = await _aipRepo.GetOfficesByAipIdAsync(primaryAip.Id, ct);
            List<int> hostAipOfficeIds = aipOffices
                .Where(o => o.OfficeId == host.Id)
                .Select(o => o.Id)
                .ToList();

            IReadOnlyList<AipProgramRollupDto> programRollups =
                await _aipRepo.GetProgramRollupsAsync(hostAipOfficeIds, ct);

            IReadOnlyList<ProgramAssignmentDto> assignments =
                await _allocationService.GetProgramAssignmentsAsync(host.Id, fiscalYear, ct);
            Dictionary<string, IReadOnlyList<int>> divisionsByProgramRefCode = assignments
                .GroupBy(a => a.ProgramRefCode, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => (IReadOnlyList<int>)g.SelectMany(a => a.DivisionIds).Distinct().ToList(),
                    StringComparer.OrdinalIgnoreCase);

            foreach (AipProgramRollupDto rollup in programRollups)
            {
                if (!divisionsByProgramRefCode.TryGetValue(rollup.ProgramRefCode, out IReadOnlyList<int>? divisionIds))
                    continue; // Unassigned PPA — belongs to no division row. Surfaced by the
                              // allocation-setup panel's "unassigned" count, not silently spread.

                foreach (int divisionId in divisionIds)
                {
                    (int Costed, int Total, decimal Amount) acc = aipByDivision.GetValueOrDefault(divisionId);
                    aipByDivision[divisionId] = (
                        acc.Costed + rollup.CostedActivityCount,
                        acc.Total  + rollup.ActivityCount,
                        acc.Amount + rollup.CostedTotal);
                }
            }
        }

        // The per-fund Used/Remaining breakdown stays the WFP ledger figure it has always been
        // (RAL-176). Decisions 3 and 4 retire WFP from what the PAGE reports, not from this
        // payload's fund rows — DivisionFundAmountDto is unchanged by the spec, and zeroing a
        // field the Allocation page's ledger view depends on would be a silent data regression.
        // One grouped query for every division across every fund; the naive alternative is a
        // per-division-per-fund N+1 inside the loop below.
        IReadOnlyList<DivisionFundUsedAmountDto> usedAmounts = await _ledgerRepo.SumUsedAmountsByDivisionsAsync(
            divisions.Select(d => d.Id).ToList(), fiscalYear, ct);
        Dictionary<(int DivisionId, int FundingSourceId), decimal> usedByDivisionFund =
            usedAmounts.ToDictionary(u => (u.DivisionId, u.FundingSourceId), u => u.UsedAmount);

        List<DivisionSummaryDto> result = [];
        foreach (Division division in divisions)
        {
            List<DivisionFundAmountDto> allocationByFund = activeFunds
                .Select(fund =>
                {
                    decimal amount = allocationsByFund[fund.Id]
                        .FirstOrDefault(a => a.DivisionId == division.Id)?.Amount ?? 0m;
                    decimal used = usedByDivisionFund.GetValueOrDefault((division.Id, fund.Id));
                    return new DivisionFundAmountDto(fund.Id, fund.Code, fund.Name, amount, used, amount - used);
                })
                .Where(f => f.Amount > 0m)
                .ToList();

            (int Costed, int Total, decimal Amount) aip = aipByDivision.GetValueOrDefault(division.Id);
            decimal allocated = allocationByFund.Sum(f => f.Amount);

            result.Add(new DivisionSummaryDto(
                division.Id, division.Code, division.Name,
                allocated,
                aip.Amount,
                allocated - aip.Amount,
                aip.Costed,
                aip.Total,
                PlanningStage.ForAip(primaryAip?.Status, aip.Total),
                // Constant until Phase 4 adds a submission entity — spec §7. Rendered rather than
                // omitted so the layout does not move when it becomes real.
                PlanningStage.Todo,
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
        // GetRecentAsync pushes ORDER BY, WHERE (office filter + table scope), and TOP(10) to SQL.
        // Actor name is read from the pre-loaded ChangedBy navigation (one JOIN, no second query).
        IReadOnlyList<AuditLog> audits = await _auditRepo.GetRecentAsync(
            10, officeId, BudgetPlanningTableNames, cancellationToken);

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
    public async Task<ServiceResult<IReadOnlyList<OfficeSummaryDto>>> GetOfficesAsync(
        User caller, int fiscalYear, CancellationToken ct = default)
    {
        // ── Scope. The load-bearing part; read IBudgetPlanningDashboardService.GetOfficesAsync
        // and OfficeScope's own remarks before touching it. Never OfficeScope.Resolve: teaching
        // it either grant would promote a cross-office READER into a cross-office EDITOR at every
        // write path that shares Resolve, with no diff at any write site to notice it.
        bool canReviewAllOffices = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        bool canManagePboCeiling = await _permissions.CanManagePboCeilingAsync(caller, ct);

        OfficeScope scope;
        if (canReviewAllOffices)
            scope = OfficeScope.ResolveForReview(caller, true);
        else if (canManagePboCeiling)
            scope = OfficeScope.ResolveForCeiling(caller, true);
        else
            // Forbidden, not an empty list — an empty list reads as "no offices exist", and a
            // caller scoped to a single office has GetOfficeDashboardAsync to call instead.
            return ServiceResult<IReadOnlyList<OfficeSummaryDto>>.Forbidden(
                "You do not have access to Budget Planning.");

        List<Office> offices = (await _officeRepo.GetAllAsync(ct))
            .Where(o => o.IsActive && scope.Permits(o.Id))
            .OrderByDescending(o => o.IsHostOffice)
            .ThenBy(o => o.OfficeCode)
            .ToList();
        if (offices.Count == 0) return ServiceResult<IReadOnlyList<OfficeSummaryDto>>.Ok([]);

        List<int> officeIds = offices.Select(o => o.Id).ToList();

        // Three aggregate reads for the whole table, awaited sequentially. The per-office
        // alternative — GetOfficeDashboardAsync in a loop — is four queries per office plus a
        // ceiling read, i.e. ~70 round trips for fourteen offices.
        IReadOnlyList<BudgetCeiling> ceilings = await _ceilingRepo.GetByFiscalYearAsync(fiscalYear, ct);
        Dictionary<int, decimal> ceilingByOffice = ceilings
            .GroupBy(c => c.OfficeId)
            .ToDictionary(g => g.Key, g => g.Sum(c => c.Amount));

        IReadOnlyDictionary<int, string> reviewerByOffice =
            await _userRepo.GetReviewerNamesByOfficeAsync(officeIds, ct);

        AipRecord? aip = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, ct);
        Dictionary<int, (int ActivityCount, decimal Costed)> aipByOffice =
            await BuildAipRollupByOfficeAsync(aip, offices, ct);

        List<OfficeSummaryDto> rows = [];
        foreach (Office office in offices)
        {
            (int ActivityCount, decimal Costed) figures = aipByOffice.GetValueOrDefault(office.Id);

            // Null vs 0m matters: null is "PBO has not published a ceiling", 0m is a published
            // decision. The UI renders stage 1 differently for each, so do not coalesce.
            decimal? ceiling = ceilingByOffice.TryGetValue(office.Id, out decimal c) ? c : null;

            rows.Add(new OfficeSummaryDto(
                office.Id,
                office.OfficeCode,
                office.OfficeName,
                office.IsHostOffice,
                ceiling,
                figures.Costed,
                figures.ActivityCount,
                PlanningStage.ForAip(aip?.Status, figures.ActivityCount),
                PlanningStage.Todo,      // Phase 4 — spec §7.
                ceiling is decimal limit && figures.Costed > limit,
                reviewerByOffice.GetValueOrDefault(office.Id)));
        }

        return ServiceResult<IReadOnlyList<OfficeSummaryDto>>.Ok(rows);
    }

    /// <summary>
    /// OfficeId → (activity count, costed total) for every office in <paramref name="offices"/>,
    /// from one grouped query over the fiscal year's AIP (PPDO-20).
    ///
    /// An office is matched to its AIP rows by <see cref="Office.OfficeRefCode"/> suffix, the same
    /// rule <c>BuildOfficeAipSummaryAsync</c> has always used — AipOffice.RefCode is a full
    /// BOM-segment code whose tail is the office's own. An office with no ref code configured
    /// cannot be matched at all and is simply absent, which reads as Todo on its row.
    /// </summary>
    private async Task<Dictionary<int, (int ActivityCount, decimal Costed)>> BuildAipRollupByOfficeAsync(
        AipRecord? aip, IReadOnlyList<Office> offices, CancellationToken ct)
    {
        if (aip is null) return [];

        IReadOnlyList<AipOfficeRollupDto> rollups = await _aipRepo.GetOfficeRollupsAsync(aip.Id, ct);

        Dictionary<int, (int ActivityCount, decimal Costed)> byOffice = [];
        foreach (Office office in offices)
        {
            if (office.OfficeRefCode is null) continue;

            List<AipOfficeRollupDto> matched = rollups
                .Where(r => r.OfficeId == office.Id)
                .ToList();
            if (matched.Count == 0) continue;

            byOffice[office.Id] =
                (matched.Sum(r => r.ActivityCount), matched.Sum(r => r.CostedTotal));
        }

        return byOffice;
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
            return new OfficeAipSummaryDto(false, null, 0, 0, 0, 0m);

        AipRecord? aipRecord = await _aipRepo.GetLatestByFiscalYearAsync(fiscalYear, cancellationToken);
        if (aipRecord is null)
            return new OfficeAipSummaryDto(false, null, 0, 0, 0, 0m);

        IReadOnlyList<AipOffice> aipOffices =
            await _aipRepo.GetOfficesByAipIdAsync(aipRecord.Id, cancellationToken);
        List<AipOffice> matched = aipOffices
            .Where(o => o.OfficeId == office.Id)
            .ToList();
        if (matched.Count == 0)
            return new OfficeAipSummaryDto(false, aipRecord.Status, 0, 0, 0, 0m);

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
            true, aipRecord.Status, programs.Count, projects.Count, activities.Count,
            // The office's OWN costed total. Summed from the activities already loaded above —
            // no extra query — and deliberately NOT the sum of the per-division rows, which
            // double-counts a PPA shared by two divisions. See the DTO's own remarks.
            activities.Sum(a => a.Total ?? 0m));
    }
}
