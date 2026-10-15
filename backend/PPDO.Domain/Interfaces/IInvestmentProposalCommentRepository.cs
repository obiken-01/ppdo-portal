using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="InvestmentProposalComment"/> (v1.9.0 — PPDO-219). Scope is the
/// caller's job: every method here trusts that the service has already checked the proposal.
/// </summary>
public interface IInvestmentProposalCommentRepository
{
    /// <summary>The proposal's comments, oldest first, with author and resolver names. Untracked.</summary>
    Task<IReadOnlyList<InvestmentProposalComment>> GetByProposalIdAsync(int proposalId, CancellationToken ct = default);

    /// <summary>One comment, <b>tracked</b> so it can be resolved in place. Null when there is none.</summary>
    Task<InvestmentProposalComment?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>The proposal's open <b>head</b> comments, replies excluded: one <c>COUNT</c> (the Finalize gate).</summary>
    Task<int> CountOpenAsync(int proposalId, CancellationToken ct = default);

    /// <summary>Open head comments per proposal, for the list pill: one grouped query. Proposals with none are absent.</summary>
    Task<IReadOnlyDictionary<int, int>> CountOpenByProposalIdsAsync(
        IReadOnlyList<int> proposalIds, CancellationToken ct = default);

    /// <summary>Stages a comment. Written by <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(InvestmentProposalComment comment, CancellationToken ct = default);

    /// <summary>Saves pending changes on the shared context.</summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
