using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="INotificationAudience"/> (PPDO-221).</summary>
public sealed class NotificationAudience : INotificationAudience
{
    private readonly IUserRepository       _users;
    private readonly IAllocationRepository _allocation;
    private readonly IPermissionService    _permissions;

    public NotificationAudience(IUserRepository users, IAllocationRepository allocation, IPermissionService permissions)
    {
        _users       = users;
        _allocation  = allocation;
        _permissions = permissions;
    }

    public Task<IReadOnlyList<User>> ProposalReadersAsync(AipOffice office, AipProgram program, CancellationToken ct = default)
        => ProgramReadersAsync(office, program, includeCrossOffice: true, ct);

    public Task<IReadOnlyList<User>> ProposalOfficeReadersAsync(AipOffice office, AipProgram program, CancellationToken ct = default)
        => ProgramReadersAsync(office, program, includeCrossOffice: false, ct);

    public Task<IReadOnlyList<User>> ActivityReadersAsync(AipOffice office, AipProgram program, CancellationToken ct = default)
        => ProgramReadersAsync(office, program, includeCrossOffice: false, ct);

    public async Task<IReadOnlyList<User>> OfficeAipReadersAsync(int officeId, CancellationToken ct = default)
    {
        List<User> readers = [];
        foreach (User u in await _users.GetNotificationCandidatesAsync(officeId, includeCrossOffice: false, ct))
        {
            if (u.OfficeId != officeId || !await _permissions.CanAccessBudgetPlanningAsync(u, ct)) continue;

            // Both axes, as BudgetPlanningScope asks. A guest office's division axis is All, so only a
            // host-office user with no division (Nothing) is dropped here.
            BudgetPlanningScope scope = BudgetPlanningScope.Resolve(u);
            if (scope.Office.Permits(officeId) && !scope.Division.SeeNothing) readers.Add(u);
        }
        return readers;
    }

    public async Task<IReadOnlyList<User>> DivisionMembersAsync(
        int officeId, IReadOnlyCollection<int> divisionIds, CancellationToken ct = default)
    {
        if (divisionIds.Count == 0) return [];
        return (await OfficeAipReadersAsync(officeId, ct))
            .Where(u => u.DivisionId is int d && divisionIds.Contains(d))
            .ToList();
    }

    public async Task<IReadOnlyList<User>> DepartmentHeadsAsync(int officeId, CancellationToken ct = default)
    {
        List<User> heads = [];
        foreach (User u in await _users.GetNotificationCandidatesAsync(officeId, includeCrossOffice: false, ct))
        {
            // ⚠️ An office comparison, never OfficeScope: a host-office reviewer resolves to SeeAll and
            // would otherwise be every office's department head (Permission Matrix §3.2, §4a).
            if (u.OfficeId != officeId || !await _permissions.CanAccessBudgetPlanningAsync(u, ct)) continue;
            if (u.Role is UserRole.SuperAdmin or UserRole.Admin
                || await _permissions.CanReviewBudgetPlanningAsync(u, ct))
                heads.Add(u);
        }
        return heads;
    }

    public async Task<IReadOnlyList<User>> CrossOfficeReviewersAsync(CancellationToken ct = default)
    {
        List<User> reviewers = [];
        foreach (User u in await _users.GetNotificationCandidatesAsync(null, includeCrossOffice: true, ct))
            if (await _permissions.CanAccessBudgetPlanningAsync(u, ct)
                && await _permissions.CanReviewAllOfficesAsync(u, ct))
                reviewers.Add(u);
        return reviewers;
    }

    private async Task<IReadOnlyList<User>> ProgramReadersAsync(
        AipOffice office, AipProgram program, bool includeCrossOffice, CancellationToken ct)
    {
        // An AIP office the backfill could not match to an office is the host's alone to read
        // (AipReadScope.FilterOffices); it has no office users to tell.
        if (office.OfficeId is null && !includeCrossOffice) return [];

        IReadOnlyList<User> candidates = await _users.GetNotificationCandidatesAsync(office.OfficeId, includeCrossOffice, ct);

        // The office's program → division rows, loaded once and only if someone's division narrows.
        // See ProposalReadRule: no other office's assignments are ever read for this program.
        IReadOnlyList<ProgramDivision>? assignments = null;

        List<User> readers = [];
        foreach (User u in candidates)
        {
            if (!includeCrossOffice && u.OfficeId != office.OfficeId) continue;
            if (!await _permissions.CanAccessBudgetPlanningAsync(u, ct)) continue;

            if (assignments is null && office.OfficeId is int oid
                && AipReadScope.Resolve(u).OfficeIdForAssignments == oid)
                assignments = await _allocation.GetProgramDivisionsByOfficeIdAsync(oid, ct);

            bool crossOffice = await _permissions.CanReviewAllOfficesAsync(u, ct);
            if (ProposalReadRule.Permits(u, crossOffice, office, program, assignments ?? []))
                readers.Add(u);
        }
        return readers;
    }
}
