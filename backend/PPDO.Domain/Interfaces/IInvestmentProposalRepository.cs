using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="InvestmentProposal"/> and its child rows (PPDO-154,
/// <c>docs/v1.8/Investment_Proposal_Spec.md</c> §5 "Repository").
///
/// <para>
/// The list and project-picker projections (<c>GET /proposals</c>, <c>GET /proposals/projects</c>)
/// are added with the service in PPDO-155: they take the caller's read scope and return its DTOs,
/// and both are designed there.
/// </para>
/// </summary>
public interface IInvestmentProposalRepository
{
    /// <summary>
    /// One proposal with every child collection, <b>tracked</b> so an update or delete can work on
    /// it in place. Null when there is none. Scope is the caller's job: check the project's office
    /// before returning anything.
    /// </summary>
    Task<InvestmentProposal?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>The proposal for one AIP project, header only and untracked. Null when there is none.</summary>
    Task<InvestmentProposal?> GetByProjectIdAsync(int aipProjectId, CancellationToken ct = default);

    /// <summary>Whether the project has a proposal (decision 26's delete guard). One <c>EXISTS</c> query.</summary>
    Task<bool> ExistsForProjectAsync(int aipProjectId, CancellationToken ct = default);

    /// <summary>Stages a new proposal and its children. Written by <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(InvestmentProposal proposal, CancellationToken ct = default);

    /// <summary>Stages a delete. The child rows go with it (cascade).</summary>
    void Remove(InvestmentProposal proposal);

    /// <summary>
    /// Makes the next save of this tracked proposal succeed only if the stored row still carries
    /// <paramref name="expectedRowVersion"/>, the version the client loaded (decision 23).
    ///
    /// <para>
    /// ⚠️ Assigning <see cref="InvestmentProposal.RowVersion"/> does not do this. EF checks the
    /// version it read from the database, not the property's current value, so without this call
    /// a stale client would overwrite a newer save.
    /// </para>
    /// </summary>
    void SetExpectedRowVersion(InvestmentProposal proposal, byte[] expectedRowVersion);

    /// <summary>
    /// Saves every pending change on the shared context.
    /// <para>
    /// ⚠️ Throws <see cref="PPDO.Domain.Common.ConcurrencyConflictException"/> when the row version
    /// no longer matches (nothing is written), and
    /// <see cref="PPDO.Domain.Common.UniqueConstraintViolationException"/> when a second proposal for
    /// the same project loses the race on <c>UX_investment_proposals_aip_project_id</c>.
    /// </para>
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
