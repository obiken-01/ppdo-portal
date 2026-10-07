using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// The PGOM Investment Proposal (v1.8.0 Demo 2.15 — PPDO-155, <c>docs/v1.8/Investment_Proposal_Spec.md</c>).
///
/// <para>
/// Endpoints gate on <c>CanAccessBudgetPlanningAsync</c> and route every write through
/// <c>ConfigHttp.AuthorizeWriteAsync</c>. This service applies the caller's office and division
/// scope itself (§3.2), and answers <b>404 "Proposal not found."</b> for a proposal that is missing
/// and for one outside the caller's scope alike, so existence is never confirmed.
/// </para>
/// </summary>
public interface IInvestmentProposalService
{
    /// <summary>AIP projects in scope for the year, each with its proposal status. Paged; pageSize ≤ 100.</summary>
    Task<ServiceResult<ProposalListPageDto>> ListAsync(
        int fiscalYear, int? officeId, string? search, int page, int pageSize, User caller,
        CancellationToken ct = default);

    /// <summary>One office's projects for the picker, unpaged. A caller who sees several offices must name one.</summary>
    Task<ServiceResult<IReadOnlyList<ProposalProjectOptionDto>>> ListProjectOptionsAsync(
        int fiscalYear, int? officeId, User caller, CancellationToken ct = default);

    /// <summary>
    /// PPDO-180 — the dashboard band for one office: counts by status and the first few projects
    /// still needing a proposal, in exactly the list's scope (office clamped, division axis applied).
    /// Null for FY2027 and earlier, or when the year has no AIP record: the band hides.
    /// </summary>
    Task<OfficeProposalSummaryDto?> GetOfficeSummaryAsync(
        int officeId, int fiscalYear, User caller, CancellationToken ct = default);

    /// <summary>
    /// PPDO-180 — counts by status per config office, in the caller's read scope (every office for
    /// a cross-office reviewer). One grouped query. Empty for FY2027 and earlier or no record.
    /// </summary>
    Task<IReadOnlyDictionary<int, ProposalCountsDto>> CountByOfficeAsync(
        int fiscalYear, User caller, CancellationToken ct = default);

    Task<ServiceResult<ProposalDto>> CreateAsync(int aipProjectId, User caller, CancellationToken ct = default);

    Task<ServiceResult<ProposalDto>> GetAsync(int id, User caller, CancellationToken ct = default);

    Task<ServiceResult<ProposalDto>> UpdateAsync(int id, UpdateProposalDto dto, User caller, CancellationToken ct = default);

    Task<ServiceResult<ProposalDto>> FinalizeAsync(int id, string? rowVersion, User caller, CancellationToken ct = default);

    Task<ServiceResult<ProposalDto>> ReopenAsync(int id, string? rowVersion, User caller, CancellationToken ct = default);

    Task<ServiceResult<bool>> DeleteAsync(int id, string? rowVersion, User caller, CancellationToken ct = default);
}
