using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Application.Common;

/// <summary>
/// Who may read an office's initial WFP, the report read off the consolidated AIP for an entered
/// year (v1.9.0 — PPDO-224, <c>Initial_WFP_Report_Spec.md</c> decision 6). Pure, so
/// <c>Permission_Matrix.md</c> §3.4 can pin each row against it.
///
/// <list type="bullet">
///   <item><description>A PPDO reader, who holds <c>CanAccessWfp</c> (the WFP report's readers today): every office.</description></item>
///   <item><description>An office's <b>department head</b> with <c>CanAccessBudgetPlanning</c>: their own office only, by <see cref="OfficeScope.Resolve"/>.</description></item>
/// </list>
///
/// <para>
/// ↩️ <b>PPDO-240 (2026-10-20): "an office's own users" became "an office's department heads".</b> A head
/// is Admin/SuperAdmin, or holds <c>CanReviewBudgetPlanning</c> or <c>CanReviewAllOffices</c>. A guest
/// office's encoder (Staff with neither grant) reads no initial WFP at all, and the endpoints refuse them
/// with 403 before any office is looked up (<see cref="MayReadAny"/>). ⚠️ Host-office readers are
/// untouched: they hold <c>CanAccessWfp</c>, which is checked first.
/// </para>
///
/// <para>
/// ⚠️ <b>Never <see cref="OfficeScope.ResolveForReview"/>.</b> A cross-office reviewer seated in a
/// guest office reads every office's AIP for review, but that is review scope, not the WFP report's:
/// they read their own office's initial WFP like any other head there. ⚠️ A user with no office resolves
/// to <see cref="OfficeScope.NoOffice"/> and reads nothing, unless <c>CanAccessWfp</c> (SuperAdmin) says otherwise.
/// </para>
///
/// <para>
/// This answers <i>who</i> and <i>which office</i>. Whether the office is offered at all (every AIP group
/// Consolidated, else 409) and which division (RAL-136) are answered after it, by the service.
/// </para>
/// </summary>
public static class InitialWfpReadRule
{
    /// <summary>The caller's resolved flags, from <c>PermissionService</c>.</summary>
    public sealed record Flags(
        bool CanAccessWfp, bool CanAccessBudgetPlanning, bool CanReviewBudgetPlanning, bool CanReviewAllOffices);

    /// <summary>
    /// Whether the caller may read <i>some</i> office's initial WFP: the endpoints' front door (PPDO-240).
    /// False for a guest-office encoder, so they get 403 rather than an empty office picker.
    /// </summary>
    public static bool MayReadAny(User caller, Flags flags)
        => flags.CanAccessWfp || (flags.CanAccessBudgetPlanning && IsDepartmentHead(caller, flags));

    /// <summary>Whether the caller may read <paramref name="officeId"/>'s initial WFP.</summary>
    public static bool Permits(User caller, int officeId, Flags flags)
        => flags.CanAccessWfp
        || (MayReadAny(caller, flags) && OfficeScope.Resolve(caller).Permits(officeId));

    private static bool IsDepartmentHead(User caller, Flags flags)
        => caller.Role is UserRole.Admin or UserRole.SuperAdmin
        || flags.CanReviewBudgetPlanning || flags.CanReviewAllOffices;
}
