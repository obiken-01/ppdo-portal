using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Who may open an Investment Proposal, as a pure rule (v1.9.0 — PPDO-221). Extracted from
/// <c>InvestmentProposalService.CanReadAsync</c> so the proposal read and the notification audience
/// answer the question with the same code and cannot drift apart.
///
/// <para>
/// The office axis is the review read scope (<see cref="OfficeScope.ResolveForReview"/>: a
/// cross-office reviewer reads every office); the division axis is AIP Entry's, on the proposal's
/// program (<see cref="AipReadScope.FilterPrograms"/>).
/// </para>
///
/// <para>
/// <paramref name="ownAssignments"/> are the <see cref="ProgramDivision"/> rows of the caller's own
/// office. They are only read when that office is the proposal's: <see cref="AipReadScope"/> never
/// narrows another office's programs. So a caller resolving many users against one proposal may pass
/// the <b>proposal office's</b> assignments for all of them and get the same answer as each user's own
/// read.
/// </para>
/// </summary>
public static class ProposalReadRule
{
    public static bool Permits(
        User user, bool canReviewAllOffices, AipOffice office, AipProgram program,
        IReadOnlyList<ProgramDivision> ownAssignments)
    {
        if (!OfficeScope.ResolveForReview(user, canReviewAllOffices).Permits(office.OfficeId)) return false;
        return AipReadScope.Resolve(user).FilterPrograms([program], [office], ownAssignments).Count == 1;
    }
}
