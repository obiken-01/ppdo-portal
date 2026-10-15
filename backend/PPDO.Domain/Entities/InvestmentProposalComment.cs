using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// One comment on an Investment Proposal (v1.9.0 — PPDO-219,
/// <c>docs/v1.9/Investment_Proposal_Comments_Spec.md</c>).
///
/// <para>
/// One thread per proposal: not anchored to a section or field. A comment is a record — there is no
/// edit, no delete and no un-resolve; a wrong one is answered by a follow-up. An open comment
/// blocks Finalize. <b>⚠️ Only <see cref="AuthorSide"/> may resolve it</b> (see
/// <see cref="ProposalCommentSide"/>). It goes with its proposal when a Draft is deleted (cascade).
/// </para>
/// </summary>
public sealed class InvestmentProposalComment
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the proposal. Cascade.</summary>
    public int ProposalId { get; set; }

    /// <summary>Plain text, 1–2,000 characters.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>Which side wrote it. Stored, never re-derived.</summary>
    public ProposalCommentSide AuthorSide { get; set; }

    /// <summary>Who wrote it.</summary>
    public Guid AuthorId { get; set; }

    /// <summary>When it was written (UTC).</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>When it was resolved (UTC), or null while open.</summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>Who resolved it. Always on <see cref="AuthorSide"/>, not necessarily the author.</summary>
    public Guid? ResolvedById { get; set; }

    /// <summary>True while the comment still blocks Finalize.</summary>
    public bool IsOpen => ResolvedAt is null;

    public InvestmentProposal Proposal { get; set; } = null!;
    public User? Author { get; set; }
    public User? ResolvedBy { get; set; }
}
