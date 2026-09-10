using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// The codebase's first <b>subtractive</b> permission (v1.8.0 — RAL-256): it takes a write away
/// rather than granting one.
///
/// Every other flag here is purely additive, and the existing idiom
/// <c>ConfigHttp.AuthorizeAsync(req, _jwt, CanX, ct)</c> cannot express "deny if the caller has
/// X" — a predicate that returns false is indistinguishable from a missing grant. So the denial
/// gets its own guard, applied uniformly, rather than an inline <c>if</c> per endpoint, which is
/// how one endpoint ends up missed.
///
/// <b>⚠️ The rule is NOT "reviewers cannot write" — there are two reviewer kinds and they differ
/// on exactly this point</b> (settled 2026-08-26, tracker B11; <c>Phase_Plan.md</c> V18-04):
/// <list type="table">
///   <item>
///     <term>Department-head reviewer (<c>CanReviewBudgetPlanning</c>, RAL-244)</term>
///     <description><b>May edit.</b> They check their office's work and update minor details they
///     find. Denying them would freeze them out of the edits the review exists to make.</description>
///   </item>
///   <item>
///     <term>PPDO consolidated reviewer (<c>CanReviewAllOffices</c>, RAL-257)</term>
///     <description><b>Comment only.</b> They review what every office submitted and must not
///     alter another office's numbers. This is the flag the denial keys on.</description>
///   </item>
/// </list>
/// An earlier reading of V18-04 had a single reviewer flag denying writes for both. That reading
/// is superseded; RAL-244's ticket carries the same warning.
///
/// <b>SuperAdmin is exempt, deliberately.</b> <c>CanReviewAllOfficesAsync</c> resolves true for
/// SuperAdmin — as every flag does, so support access always works — so a guard that simply asked
/// "is this a cross-office reviewer?" would lock SuperAdmin out of every write in budget planning.
/// The role's blanket bypass exists to GRANT access for support; reading it as grounds to impose
/// a restriction inverts its purpose. Pinned by
/// <c>ReviewerWriteGuardTests.DeniesWriteAsync_SuperAdmin_IsNeverDenied</c>.
///
/// Lives in Application rather than Functions because <c>PPDO.Functions</c> has no
/// <c>InternalsVisibleTo("PPDO.Tests")</c> — the testable logic belongs here and
/// <c>ConfigHttp.AuthorizeWriteAsync</c> is a thin wrapper over it.
/// </summary>
public static class ReviewerWriteGuard
{
    /// <summary>
    /// Whether <paramref name="user"/> is denied write access to budget-planning content.
    ///
    /// True only for a non-SuperAdmin holding the cross-office review grant. Everyone else —
    /// including a department-head reviewer, and including any caller holding no reviewer flag
    /// at all — is unaffected, so applying this guard to an endpoint changes nothing for the
    /// people already using it.
    ///
    /// Content only. Submitting for review, returning a submission, and leaving comments are
    /// the reviewer's own actions and must NOT be routed through this guard when Phase 4 adds
    /// them — a comment-only reviewer who cannot comment is not a reviewer.
    /// </summary>
    public static async Task<bool> DeniesWriteAsync(
        User user,
        IPermissionService permissions,
        CancellationToken cancellationToken = default)
    {
        // Support access wins. See the type's remarks — this is the decision RAL-256 required
        // to be made explicitly rather than inherited from the flag's resolution.
        if (user.Role is UserRole.SuperAdmin) return false;

        return await permissions.CanReviewAllOfficesAsync(user, cancellationToken);
    }
}
