using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Budget Planning Dashboard data service (RAL-80, RAL-92, RAL-60).
/// GetDashboardAsync fetches small config tables in full (appropriate for their size).
/// GetRecentActivityAsync delegates to IAuditRepository.GetRecentAsync so the DB
/// applies ordering, office filtering, and TAKE — the entire audit_log is never loaded.
/// GetOfficeDashboardAsync composes the office-scoped readiness hub by calling
/// IAllocationService for the allocation-setup panel — it never re-implements those queries.
/// </summary>
public sealed class BudgetPlanningDashboardService : IBudgetPlanningDashboardService
{
    private readonly IRepository<LdipRecord> _ldipRepo;
    private readonly IAipRepository          _aipRepo;
    private readonly IRepository<WfpRecord>  _wfpRepo;
    private readonly IRepository<Office>     _officeRepo;
    private readonly IAuditRepository        _auditRepo;
    private readonly IAllocationService      _allocationService;

    public BudgetPlanningDashboardService(
        IRepository<LdipRecord> ldipRepo,
        IAipRepository          aipRepo,
        IRepository<WfpRecord>  wfpRepo,
        IRepository<Office>     officeRepo,
        IAuditRepository        auditRepo,
        IAllocationService      allocationService)
    {
        _ldipRepo          = ldipRepo;
        _aipRepo           = aipRepo;
        _wfpRepo           = wfpRepo;
        _officeRepo        = officeRepo;
        _auditRepo         = auditRepo;
        _allocationService = allocationService;
    }

    /// <inheritdoc />
    public async Task<PlanningDashboardDto> GetDashboardAsync(
        int? fiscalYear, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<LdipRecord> ldips   = await _ldipRepo.GetAllAsync(cancellationToken);
        IReadOnlyList<AipRecord>  aips    = await _aipRepo.GetAllAsync(cancellationToken);
        IReadOnlyList<WfpRecord>  wfps    = await _wfpRepo.GetAllAsync(cancellationToken);
        IReadOnlyList<Office>     offices = await _officeRepo.GetAllAsync(cancellationToken);

        // Available fiscal years — distinct, newest first.
        List<int> availableFiscalYears = aips
            .Select(a => a.FiscalYear)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

        // Resolved FY: explicit param → latest AIP year → next calendar year.
        int resolvedFY = fiscalYear
            ?? (availableFiscalYears.Count > 0 ? availableFiscalYears[0] : DateTime.UtcNow.Year + 1);

        // LDIP summary.
        List<LdipRecord> allLdips = [.. ldips];
        List<StatusBreakdownDto> ldipBreakdown = allLdips
            .GroupBy(l => l.Status)
            .Select(g => new StatusBreakdownDto(g.Key, g.Count()))
            .ToList();
        LdipSummaryDto ldipSummary = new(allLdips.Count, ldipBreakdown);

        // AIP summary for the resolved FY.
        List<AipRecord> fyAips = aips.Where(a => a.FiscalYear == resolvedFY).ToList();
        List<StatusBreakdownDto> aipBreakdown = fyAips
            .GroupBy(a => a.Status)
            .Select(g => new StatusBreakdownDto(g.Key, g.Count()))
            .ToList();
        AipSummaryDto aipSummary = new(fyAips.Count, aipBreakdown);

        // Primary AIP for the resolved FY: prefer Final, then Draft, then newest by id.
        static int AipStatusRank(string s) => s == "Final" ? 0 : s == "Draft" ? 1 : 2;
        AipRecord? primaryAip = fyAips
            .OrderBy(a => AipStatusRank(a.Status))
            .ThenByDescending(a => a.Id)
            .FirstOrDefault();

        // WFP map keyed by OfficeId (for the primary AIP only).
        Dictionary<int, WfpRecord> wfpMap = primaryAip is null
            ? []
            : wfps.Where(w => w.AipRecordId == primaryAip.Id)
                  .ToDictionary(w => w.OfficeId);

        // Active offices left-joined to WFP map → status rows, sorted Not started → Draft → Final.
        List<Office> activeOffices = offices.Where(o => o.IsActive).ToList();
        static int WfpOfficeStatusRank(string s) => s == "Not started" ? 0 : s == "Draft" ? 1 : 2;
        List<WfpOfficeStatusDto> wfpByOffice = activeOffices
            .Select(o =>
            {
                wfpMap.TryGetValue(o.Id, out WfpRecord? wfp);
                return new WfpOfficeStatusDto(
                    o.Id, o.OfficeCode, o.OfficeName,
                    wfp?.Status ?? "Not started",
                    wfp?.AipRecordId);
            })
            .OrderBy(r => WfpOfficeStatusRank(r.WfpStatus))
            .ToList();

        // WFP summary: Final WFP count across ALL AIPs for the resolved FY vs active offices.
        int finalWfpCount = wfps.Count(w => w.FiscalYear == resolvedFY && w.Status == "Final");
        WfpSummaryDto wfpSummary = new(finalWfpCount, activeOffices.Count);

        return new PlanningDashboardDto(
            resolvedFY,
            availableFiscalYears,
            ldipSummary,
            aipSummary,
            wfpSummary,
            wfpByOffice);
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
                a.ChangedAt,
                a.TableName,
                a.Action,
                a.RecordId,
                a.ChangedBy?.FullName ?? "Unknown"))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<OfficeDashboardDto> GetOfficeDashboardAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken = default)
    {
        AllocationSetupSummaryDto allocation =
            await BuildAllocationSummaryAsync(officeId, fiscalYear, cancellationToken);
        OfficeLdipSummaryDto ldip = BuildOfficeLdipSummary();
        OfficeAipSummaryDto aip =
            await BuildOfficeAipSummaryAsync(officeId, fiscalYear, cancellationToken);

        return new OfficeDashboardDto(officeId, fiscalYear, allocation, ldip, aip);
    }

    private async Task<AllocationSetupSummaryDto> BuildAllocationSummaryAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken)
    {
        ServiceResult<BudgetCeilingDto> ceilingResult =
            await _allocationService.GetCeilingAsync(officeId, fiscalYear, cancellationToken);
        decimal? ceilingAmount = ceilingResult.IsSuccess ? ceilingResult.Value!.Amount : null;

        IReadOnlyList<DivisionAllocationDto> allocations =
            await _allocationService.GetAllocationsAsync(officeId, fiscalYear, cancellationToken);
        decimal allocated = allocations.Sum(a => a.Amount);

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
    /// Stubbed until RAL-61 adds ldip_records.office_id — LDIP has no office scoping
    /// today, so returning a real count here would silently show global data as if
    /// it were office-scoped. ScopingSupported=false tells the frontend to say so.
    /// </summary>
    private static OfficeLdipSummaryDto BuildOfficeLdipSummary() => new(false, 0, []);

    private async Task<OfficeAipSummaryDto> BuildOfficeAipSummaryAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken)
    {
        IReadOnlyList<Office> offices = await _officeRepo.GetAllAsync(cancellationToken);
        Office? office = offices.FirstOrDefault(o => o.Id == officeId);
        if (office?.OfficeRefCode is null)
            return new OfficeAipSummaryDto(false, null, 0, 0, 0);

        IReadOnlyList<AipRecord> allAip = await _aipRepo.GetAllAsync(cancellationToken);
        AipRecord? aipRecord = allAip.FirstOrDefault(
            r => r.FiscalYear == fiscalYear && r.Status != PlanningStatus.Archived);
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
