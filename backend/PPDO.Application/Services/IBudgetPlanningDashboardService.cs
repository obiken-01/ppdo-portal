using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

public interface IBudgetPlanningDashboardService
{
    /// <summary>
    /// The PPDO-scoped Dashboard (v1.4.5 — RAL-161). <paramref name="divisionId"/> is resolved
    /// and clamped by the caller (the Functions layer) exactly like
    /// <see cref="IWfpReportService.GetReportAsync"/>'s existing RAL-136 pattern: null means
    /// "every division" (finance/admin), a value means "this division only" (division-scoped
    /// Staff — the Function derives this from the caller's own DivisionId, never from a
    /// client-supplied query param). <see cref="PpdoDashboardDto.WfpByDivision"/> and every
    /// <see cref="FundCeilingDto.ByDivision"/> entry are filtered to match.
    /// </summary>
    Task<PpdoDashboardDto> GetDashboardAsync(
        int? fiscalYear, int? divisionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(
        int? officeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Office-scoped readiness hub (RAL-60): allocation-setup summary, LDIP program
    /// count (stubbed until RAL-61 adds ldip_records.office_id), and AIP presence +
    /// PPA/activity count for the given office+FY.
    /// </summary>
    Task<OfficeDashboardDto> GetOfficeDashboardAsync(
        int officeId, int fiscalYear, CancellationToken cancellationToken = default);
}
