using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

public interface IBudgetPlanningDashboardService
{
    Task<PlanningDashboardDto> GetDashboardAsync(
        int? fiscalYear, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<RecentActivityDto>> GetRecentActivityAsync(
        int? officeId, CancellationToken cancellationToken = default);
}
