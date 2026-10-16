namespace PPDO.Domain.Enums;

/// <summary>
/// Which side of the review authored a comment (v1.8.0 Phase 4 — V18-53 / PPDO-71).
///
/// <para>
/// <b>⚠️ Two sides resolve, and the encoder is not one of them.</b> The encoder reads comments, acts on
/// them and re-submits. ↩️ Since v1.9.0 (PPDO-223) they may <i>reply</i> inside a reviewer's thread,
/// stored as <see cref="Encoder"/>, but they never start one, never resolve, and a reply is never
/// counted. That is why the unresolved counter still splits two ways rather than three.
/// </para>
///
/// <para>
/// <b>⚠️ This is stored on the comment, not derived from the author's flags at read time.</b> Flags
/// change: a person can gain or lose <c>CanReviewAllOffices</c> months later, and if the side were
/// derived, an old comment would silently change who is allowed to resolve it. The rule in
/// <see cref="AipReviewComment"/> — only the authoring side resolves — is only enforceable if the
/// side is a fact about the comment rather than about the author today.
/// </para>
///
/// <para>
/// Stored as a string (<c>nvarchar(16)</c>), not an int: this column is read directly in SQL when
/// someone is working out why a comment could not be resolved, and <c>Ppdo</c> answers that
/// question where <c>1</c> does not.
/// </para>
/// </summary>
public enum AipCommentSide
{
    /// <summary>
    /// The office's own department-head reviewer (<c>CanReviewBudgetPlanning</c>). Their comments
    /// are resolved by them — ⚠️ <b>not</b> by the encoder they are addressed to.
    /// </summary>
    DepartmentHead = 0,

    /// <summary>
    /// A PPDO consolidated reviewer (<c>CanReviewAllOffices</c>). Their comments are resolved by
    /// PPDO — ⚠️ <b>not</b> by the office they are addressed to.
    /// </summary>
    Ppdo = 1,

    /// <summary>
    /// An encoder's reply inside a reviewer's thread (v1.9.0 — PPDO-223). ⚠️ Only ever on a reply:
    /// never a head, never resolves anything, never counted.
    /// </summary>
    Encoder = 2,
}
