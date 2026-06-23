using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Budget Planning Dashboard data service (RAL-80, RAL-92).
/// GetDashboardAsync fetches small config tables in full (appropriate for their size).
/// GetRecentActivityAsync delegates to IAuditRepository.GetRecentAsync so the DB
/// applies ordering, office filtering, and TAKE — the entire audit_log is never loaded.
/// </summary>
public sealed class BudgetPlanningDashboardService : IBudgetPlanningDashboardService
{
    private readonly IRepository<LdipRecord> _ldipRepo;
    private readonly IRepository<AipRecord>  _aipRepo;
    private readonly IRepository<WfpRecord>  _wfpRepo;
    private readonly IRepository<Office>     _officeRepo;
    private readonly IAuditRepository        _auditRepo;

    public BudgetPlanningDashboardService(
        IRepository<LdipRecord> ldipRepo,
        IRepository<AipRecord>  aipRepo,
        IRepository<WfpRecord>  wfpRepo,
        IRepository<Office>     officeRepo,
        IAuditRepository        auditRepo)
    {
        _ldipRepo   = ldipRepo;
        _aipRepo    = aipRepo;
        _wfpRepo    = wfpRepo;
        _officeRepo = officeRepo;
        _auditRepo  = auditRepo;
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
}
