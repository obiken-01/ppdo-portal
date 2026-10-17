using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Who receives a notification (v1.9.0 — PPDO-221, <c>Notification_Centre_Spec.md</c> §3.1):
/// "everyone who can open the thing", worked out with the same scope rules the target's own read uses.
///
/// <para>
/// ⚠️ <b>One user query per call, never one per user.</b> Each method loads its candidates once
/// (<c>IUserRepository.GetNotificationCandidatesAsync</c>) and decides in memory with
/// <c>PermissionService</c>, which reads no database, and the scope types.
/// </para>
///
/// <para>
/// Every method requires <c>CanAccessBudgetPlanning</c>, the gate on every endpoint these targets sit
/// behind. None of them removes the actor; <see cref="INotificationService.StageAsync"/> does that for
/// every event in one place.
/// </para>
/// </summary>
public interface INotificationAudience
{
    /// <summary>
    /// Everyone who can open a proposal on <paramref name="program"/> of <paramref name="office"/>: the
    /// office's own users the division axis lets through, and every other office's user whose review
    /// scope reaches it (host office, cross-office reviewers, SuperAdmin). Same rule as the proposal
    /// read (<c>ProposalReadRule</c>).
    /// </summary>
    Task<IReadOnlyList<User>> ProposalReadersAsync(AipOffice office, AipProgram program, CancellationToken ct = default);

    /// <summary>
    /// The office's own users who can open an activity on <paramref name="program"/>: AIP Entry's read,
    /// division axis included. Users of other offices are never in it.
    /// </summary>
    Task<IReadOnlyList<User>> ActivityReadersAsync(AipOffice office, AipProgram program, CancellationToken ct = default);

    /// <summary>
    /// The office's own users who can open its AIP (<c>BudgetPlanningScope</c>): a host-office Staff
    /// member with no division sees none of it and is left out; a guest office's unassigned Staff is in.
    /// </summary>
    Task<IReadOnlyList<User>> OfficeAipReadersAsync(int officeId, CancellationToken ct = default);

    /// <summary>The users of <see cref="OfficeAipReadersAsync"/> who sit in one of <paramref name="divisionIds"/>.</summary>
    Task<IReadOnlyList<User>> DivisionMembersAsync(
        int officeId, IReadOnlyCollection<int> divisionIds, CancellationToken ct = default);

    /// <summary>
    /// The office's department heads: its own Admin/SuperAdmin users and holders of
    /// <c>CanReviewBudgetPlanning</c> whose office is this one (Permission Matrix §3.2) — never a
    /// reviewer from another office.
    /// </summary>
    Task<IReadOnlyList<User>> DepartmentHeadsAsync(int officeId, CancellationToken ct = default);

    /// <summary>PPDO's cross-office reviewers (<c>CanReviewAllOffices</c>), wherever they sit.</summary>
    Task<IReadOnlyList<User>> CrossOfficeReviewersAsync(CancellationToken ct = default);
}
