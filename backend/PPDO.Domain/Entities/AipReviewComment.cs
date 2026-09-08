using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// One inline review comment on an AIP row (v1.8.0 Phase 4 — V18-53 / PPDO-71,
/// <c>AIP_Review_Spec.md</c> §5.1).
///
/// <para>
/// <b>⚠️ Only the authoring side may resolve.</b> An office may not resolve a PPDO reviewer's
/// comment, and an encoder may not resolve their own department head's. This is the single most
/// load-bearing rule in the phase and it is easy to build backwards, because "let the person who
/// was asked mark it done" is the intuitive reading. It is wrong: the soft gate on re-submit
/// (PPDO-72) counts unresolved comments, so if the recipient can clear them, the person being
/// asked to change something clears the ask and re-submits having addressed nothing — and the
/// failure is silent, because everything then looks resolved.
/// </para>
///
/// <para>
/// <b>⚠️ There is no threading and no reply.</b> Resolve is the only action on a comment
/// (spec decision 9). A conversation happens in the office, not in the document.
/// </para>
///
/// <para>
/// <b>⚠️ Resolved comments are marked, never deleted.</b> A resolve is a state change, so the
/// trail of what was asked and when it was cleared survives for PPDO-77's "Show History".
/// </para>
/// </summary>
public sealed class AipReviewComment
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>
    /// FK to the <c>aip_offices</c> row whose review this belongs to. Cascade — a comment cannot
    /// outlive the office row it was written against.
    ///
    /// ⚠️ The office, not the record: an office holds one <c>AipOffice</c> row per sub-office group
    /// and they move through the workflow together, so scoping a comment to the group row is what
    /// keeps "the unresolved count for this office" answerable in one query.
    /// </summary>
    public int AipOfficeId { get; set; }

    /// <summary>Which kind of node this comment is anchored to.</summary>
    public AipCommentNodeType NodeType { get; set; }

    /// <summary>
    /// The anchored node's id, in the table <see cref="NodeType"/> names.
    ///
    /// <b>⚠️ Deliberately NOT a foreign key.</b> A polymorphic anchor across three tables cannot be
    /// one. The consequence is real and must be handled rather than discovered: <b>deleting a
    /// commented node leaves this comment dangling.</b> That is accepted on purpose — the comment
    /// is part of the record of why the work changed, so the read tolerates a missing node and
    /// shows it as orphaned rather than cascading it away. Deleting the evidence that a row was
    /// questioned, because the row was then removed, would destroy exactly the history
    /// <see cref="ResolvedAt"/> exists to preserve.
    /// </summary>
    public int NodeId { get; set; }

    /// <summary>Who wrote it.</summary>
    public Guid AuthorId { get; set; }

    /// <summary>
    /// Which side wrote it. ⚠️ Stored, never re-derived from <see cref="AuthorId"/>'s current
    /// flags — see <see cref="AipCommentSide"/> for why that distinction decides whether the
    /// resolve rule is enforceable at all.
    /// </summary>
    public AipCommentSide AuthorSide { get; set; }

    /// <summary>The comment text. Max 2,000 characters.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>When it was written (UTC; rendered UTC+8).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When it was resolved, or null while it is outstanding.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Who resolved it. ⚠️ Always someone on <see cref="AuthorSide"/> — the service enforces that,
    /// and it is not the same person as <see cref="AuthorId"/> in general: any PPDO reviewer may
    /// resolve any PPDO comment (one reviewer going on leave must not strand a comment).
    /// </summary>
    public Guid? ResolvedById { get; set; }

    /// <summary>True while the comment still counts against the re-submit warning.</summary>
    public bool IsUnresolved => ResolvedAt is null;

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The office row this comment belongs to.</summary>
    public AipOffice AipOffice { get; set; } = null!;

    /// <summary>The author, for rendering a name beside the comment.</summary>
    public User? Author { get; set; }

    /// <summary>Who resolved it, if anyone.</summary>
    public User? ResolvedBy { get; set; }
}
