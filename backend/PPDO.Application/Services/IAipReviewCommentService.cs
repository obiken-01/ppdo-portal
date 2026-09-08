using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Inline review comments on AIP rows (v1.8.0 Phase 4 — V18-53 / PPDO-71,
/// <c>AIP_Review_Spec.md</c> §5.1 and decisions 7–11).
///
/// <para>
/// <b>⚠️ Only the authoring side resolves.</b> An office may not resolve a PPDO reviewer's comment,
/// and an encoder may not resolve their own department head's. The intuitive reading — "the person
/// who was asked marks it done" — is the wrong one, and building it that way fails silently: the
/// soft gate on re-submit counts unresolved comments, so a recipient who can clear them re-submits
/// having addressed nothing while the screen shows everything resolved.
/// </para>
///
/// <para>
/// <b>⚠️ Commenting is not a content write, and must not be routed through the write guards.</b>
/// <c>AipWriteGuard</c> closes an office once it reaches PPDO — which is precisely the state in
/// which a PPDO reviewer needs to comment. <c>ReviewerWriteGuard</c> denies content writes to
/// cross-office reviewers — who are the main authors here. Its own remarks say so by name: "a
/// comment-only reviewer who cannot comment is not a reviewer."
/// </para>
///
/// <para>
/// <b>⚠️ Resolve is the only action.</b> No replies, no threading, no editing, no deletion
/// (decision 9). A resolved comment is marked and kept, because PPDO-77's history reads it back.
/// </para>
/// </summary>
public interface IAipReviewCommentService
{
    /// <summary>
    /// Every comment on one office — resolved ones included — with the unresolved tally split by
    /// authoring side.
    ///
    /// Readable by anyone who can see the office: its encoders and department head, and any PPDO
    /// consolidated reviewer. Scoped by <c>OfficeScope.ResolveForReview</c>.
    /// </summary>
    Task<ServiceResult<AipReviewCommentsDto>> GetForOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default);

    /// <summary>
    /// Leaves a comment on one row.
    ///
    /// ⚠️ Refused for a caller holding neither reviewer flag — the encoder never comments. The
    /// side recorded on the comment is decided here, once, and never re-derived: see the
    /// implementation's <c>ResolveSideAsync</c>.
    /// </summary>
    Task<ServiceResult<AipReviewCommentDto>> CreateAsync(
        int aipRecordId, int officeId, CreateAipReviewCommentDto dto, User caller,
        CancellationToken ct = default);

    /// <summary>
    /// Marks one comment resolved.
    ///
    /// ⚠️ Permitted only to the side that wrote it — not to the side it is addressed to. Already
    /// resolved is refused rather than treated as success, so a double-click cannot silently
    /// reassign who cleared it.
    /// </summary>
    Task<ServiceResult<AipReviewCommentDto>> ResolveAsync(
        int commentId, User caller, CancellationToken ct = default);
}
