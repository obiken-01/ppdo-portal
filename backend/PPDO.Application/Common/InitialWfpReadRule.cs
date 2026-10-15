using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Who may read an office's initial WFP, the report read off the consolidated AIP for an entered
/// year (v1.9.0 — PPDO-224, <c>Initial_WFP_Report_Spec.md</c> decision 6). Pure, so
/// <c>Permission_Matrix.md</c> §3.4 can pin each row against it.
///
/// <list type="bullet">
///   <item><description>A PPDO reader, who holds <c>CanAccessWfp</c> (the WFP report's readers today): every office.</description></item>
///   <item><description>Anyone else with <c>CanAccessBudgetPlanning</c>: their own office only, by <see cref="OfficeScope.Resolve"/>.</description></item>
/// </list>
///
/// <para>
/// ⚠️ <b>Never <see cref="OfficeScope.ResolveForReview"/>.</b> A cross-office reviewer seated in a
/// guest office reads every office's AIP for review, but that is review scope, not the WFP report's:
/// they read their own office's initial WFP like anyone else there. ⚠️ A user with no office resolves
/// to <see cref="OfficeScope.NoOffice"/> and reads nothing, unless <c>CanAccessWfp</c> (SuperAdmin) says otherwise.
/// </para>
///
/// <para>
/// This answers <i>which office</i>. Whether the office is offered at all (every AIP group
/// Consolidated, else 409) and which division (RAL-136) are answered after it, by the service.
/// </para>
/// </summary>
public static class InitialWfpReadRule
{
    public static bool Permits(User caller, int officeId, bool canAccessWfp, bool canAccessBudgetPlanning)
        => canAccessWfp || (canAccessBudgetPlanning && OfficeScope.Resolve(caller).Permits(officeId));
}
