using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="InvestmentProposal"/> and its child rows (PPDO-154,
/// <c>docs/v1.8/Investment_Proposal_Spec.md</c> §5 "Repository").
///
/// </summary>
public interface IInvestmentProposalRepository
{
    /// <summary>
    /// AIP projects with their proposal status, for the list page and the project picker
    /// (PPDO-155, spec §4). One projection query: projects joined to their program and AIP
    /// office, left-joined to <c>investment_proposals</c> on the unique project id. Ordered by
    /// office, program and project ref code.
    /// <para>
    /// ⚠️ The query applies only the scope it is given. The service resolves the caller's office
    /// and division scope into <paramref name="query"/>; this method never reads a user.
    /// </para>
    /// </summary>
    Task<ProposalProjectPage> ListProjectsAsync(ProposalProjectQuery query, CancellationToken ct = default);

    /// <summary>Whether any of these projects has a proposal: the delete guard for programs and offices.</summary>
    Task<bool> ExistsForAnyProjectAsync(IReadOnlyList<int> aipProjectIds, CancellationToken ct = default);

    /// <summary>Funding-source names for fund codes (Source of Fund, decision 17). Codes with no row are left out.</summary>
    Task<IReadOnlyDictionary<string, string>> GetFundNamesByCodesAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default);

    /// <summary>Climate change typology names for codes (Section M, decision 13). Codes with no row are left out.</summary>
    Task<IReadOnlyDictionary<string, string>> GetTypologyNamesByCodesAsync(
        IReadOnlyList<string> codes, CancellationToken ct = default);

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

/// <summary>
/// An already-scoped filter for <see cref="IInvestmentProposalRepository.ListProjectsAsync"/>.
/// <para>
/// ⚠️ <b>Every value here is the caller's scope, resolved by the service.</b>
/// <see cref="AipOfficeIds"/> null means every AIP office of the record (a caller who sees all
/// offices). The division axis is the pair below: a program of an office in
/// <see cref="NarrowedAipOfficeIds"/> is listed only when its ref code is in
/// <see cref="AllowedProgramRefCodes"/>, the same rule as <c>AipReadScope.FilterPrograms</c>.
/// </para>
/// </summary>
public sealed record ProposalProjectQuery(
    int                   AipRecordId,
    IReadOnlyList<int>?   AipOfficeIds,
    IReadOnlyList<int>    NarrowedAipOfficeIds,
    IReadOnlyList<string> AllowedProgramRefCodes,
    string?               Search,
    int                   Skip,
    int?                  Take);

/// <summary>One AIP project and its proposal, if any (spec §4, both list shapes).</summary>
public sealed record ProposalProjectRow(
    int       AipProjectId,
    string    ProjectRefCode,
    string    ProjectName,
    int       ProgramId,
    string    ProgramRefCode,
    string    ProgramName,
    int       AipOfficeId,
    string    OfficeName,
    decimal   ProjectCost,
    int?      ProposalId,
    string?   ProposalStatus,
    DateTime? UpdatedAt,
    string?   UpdatedByName);

/// <summary>A page of <see cref="ProposalProjectRow"/>. <see cref="TotalCount"/> counts the whole match.</summary>
public sealed record ProposalProjectPage(IReadOnlyList<ProposalProjectRow> Items, int TotalCount);
