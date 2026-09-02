using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

public interface IBudgetPlanningDashboardService
{
    /// <summary>
    /// The PPDO-scoped Dashboard (v1.4.5 — RAL-161). <paramref name="divisionId"/> is resolved
    /// and clamped by the caller (the Functions layer) exactly like
    /// <see cref="IWfpReportService.GetReportAsync"/>'s existing RAL-136 pattern: null means
    /// "every division" (finance/admin), a value means "this division only" (division-scoped
    /// Staff — the Function derives this from the caller's own DivisionId, never from a
    /// client-supplied query param). <see cref="PpdoDashboardDto.ByDivision"/> and every
    /// <see cref="FundCeilingDto.ByDivision"/> entry are filtered to match.
    /// </summary>
    Task<PpdoDashboardDto> GetDashboardAsync(
        int? fiscalYear, int? divisionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The fiscal-year picker alone (RAL-166 follow-up) — for callers (the Report page) that
    /// only need <see cref="PpdoDashboardDto.FiscalYear"/>/<see cref="PpdoDashboardDto.AvailableFiscalYears"/>
    /// and would otherwise pay for the full Dashboard build just to read two fields off it.
    /// </summary>
    Task<FiscalYearsDto> GetFiscalYearsAsync(
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

    /// <summary>
    /// One row per office in <paramref name="caller"/>'s cross-office scope, for the dashboard's
    /// office table (PPDO-20).
    ///
    /// <b>Scope resolution is the load-bearing part of this method</b>, which is why it takes the
    /// caller rather than a pre-resolved office id like every other method here. The rule:
    /// <code>
    /// CanReviewAllOffices   → OfficeScope.ResolveForReview(caller, true)
    /// CanManagePboCeiling   → OfficeScope.ResolveForCeiling(caller, true)
    /// neither               → Forbidden
    /// </code>
    /// Never <c>OfficeScope.Resolve</c> — see that method's own remarks. A caller holding both
    /// resolves through <c>ResolveForReview</c>; the endpoint is read-only either way, so the
    /// distinction only decides what the UI renders.
    ///
    /// A caller with neither grant gets <see cref="ServiceResult{T}.Forbidden"/>, <b>not an empty
    /// list</b>: an empty list reads as "no offices exist", and a caller who is legitimately
    /// scoped to one office has a different endpoint to call.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<OfficeSummaryDto>>> GetOfficesAsync(
        User caller, int fiscalYear, CancellationToken cancellationToken = default);
}
