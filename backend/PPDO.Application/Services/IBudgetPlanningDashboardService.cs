using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

public interface IBudgetPlanningDashboardService
{
    Task<PlanningDashboardDto> GetDashboardAsync(
        int? fiscalYear, CancellationToken cancellationToken = default);

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
