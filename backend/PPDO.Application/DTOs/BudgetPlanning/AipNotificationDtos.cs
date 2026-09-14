namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// What is waiting on the caller, and whether their own office has been handed back to them
/// (V18-58 / PPDO-75, <c>AIP_Review_Spec.md</c> §6.5). One read per portal load.
/// </summary>
/// <param name="PendingForPpdo">Offices at <c>SubmittedToPpdo</c> across open FY2028+ years. Zero,
/// and never queried, unless the caller holds <c>CanReviewAllOffices</c>.</param>
/// <param name="PpdoFiscalYear">The earliest year with such an office — where the count links to.
/// Null when the count is zero.</param>
/// <param name="PendingForDepartmentHead">Open years in which the caller's own office sits at
/// <c>DepartmentReview</c>. Zero unless the caller holds <c>CanReviewBudgetPlanning</c>.</param>
/// <param name="DepartmentHeadFiscalYear">The earliest such year, or null.</param>
/// <param name="Returned">Open years in which the caller's own office is returned to them, earliest
/// first. Empty when nothing is.</param>
public sealed record AipReviewNotificationsDto(
    int     PendingForPpdo,
    int?    PpdoFiscalYear,
    int     PendingForDepartmentHead,
    int?    DepartmentHeadFiscalYear,
    IReadOnlyList<AipReturnedNoticeDto> Returned);

/// <summary>
/// One returned year. <paramref name="ReturnedBy"/> is <c>"Ppdo"</c> (the office sits at
/// <c>ReturnedByPpdo</c>) or <c>"DepartmentHead"</c> (it sits at <c>Draft</c> and its latest hand-off
/// was <c>RETURN_DH</c>).
/// </summary>
public sealed record AipReturnedNoticeDto(int FiscalYear, string ReturnedBy);
