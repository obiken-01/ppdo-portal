using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="AipReadScope"/> — the two-axis AIP rule (v1.8.0 Phase 2 — V18-39 / PPDO-38,
/// re-keyed off the caller's own office by PPDO-134).
///
/// <para>
/// ⚠️ <b>Every case here is a data-leak or data-loss case, and both directions are silent.</b>
/// Honour division for an office that has none configured and its people see a fraction of their
/// own AIP and report missing data; ignore it for a caller who genuinely has one and they see
/// figures outside their division. Neither throws, and neither is visible in a diff at the call
/// site — which is why the rule is tested here directly rather than only through the services
/// that consume it.
/// </para>
/// </summary>
public sealed class AipReadScopeTests
{
    private const int HostOfficeId  = 1;   // PPDO
    private const int GuestOfficeId = 2;   // e.g. GSO
    private const int PlanningDivId = 10;
    private const int RmedDivId     = 11;

    private static Office Office(int id, bool isHost) => new()
    {
        Id = id, OfficeCode = $"O{id}", OfficeName = $"Office {id}", IsHostOffice = isHost,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User Caller(int? officeId, bool isHost, int? divisionId, UserRole role = UserRole.Staff) => new()
    {
        Id = Guid.NewGuid(), Username = "u", PasswordHash = "h", FullName = "U",
        Role = role, OfficeId = officeId, DivisionId = divisionId,
        Office = officeId is int oid ? Office(oid, isHost) : null,
        IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static AipOffice AipOff(int id, int? ownerOfficeId) => new()
    {
        Id = id, AipRecordId = 100, RefCode = $"1000-000-1-01-{id:000}",
        Name = $"AIP office {id}", Sector = "General", OfficeId = ownerOfficeId,
    };

    private static AipProgram Prog(int id, int aipOfficeId, string refCode) => new()
    {
        Id = id, OfficeId = aipOfficeId, RefCode = refCode, Name = $"Program {refCode}",
    };

    private static ProgramDivision Assign(string programRefCode, int divisionId, int officeId = HostOfficeId) => new()
    {
        Id = 0, OfficeId = officeId, OfficeRefCode = "01-010",
        ProgramRefCode = programRefCode, DivisionId = divisionId,
    };

    // ── Office axis ───────────────────────────────────────────────────────────

    [Fact]
    public void FilterOffices_GuestOfficeCaller_SeesOnlyItsOwn()
    {
        AipReadScope scope = AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: null));

        IReadOnlyList<AipOffice> result = scope.FilterOffices(
            [AipOff(1, HostOfficeId), AipOff(2, GuestOfficeId), AipOff(3, 99)]);

        Assert.Equal(2, Assert.Single(result).Id);
    }

    [Fact]
    public void FilterOffices_HostOfficeCaller_SeesEveryOffice()
    {
        AipReadScope scope = AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: null));

        IReadOnlyList<AipOffice> result = scope.FilterOffices(
            [AipOff(1, HostOfficeId), AipOff(2, GuestOfficeId), AipOff(3, null)]);

        Assert.Equal(3, result.Count);   // including the unmatched row — PPDO reviews everything
    }

    [Fact]
    public void FilterOffices_CallerWithNoOffice_SeesNothing()
    {
        // ⚠️ Null office_id means UNASSIGNED, not privileged. Until DECISION F (RAL-258) it
        // positively meant "PPDO, sees everything" — the inverse. Comments asserting that are stale.
        AipReadScope scope = AipReadScope.Resolve(Caller(officeId: null, isHost: false, divisionId: null));

        Assert.Empty(scope.FilterOffices([AipOff(1, HostOfficeId), AipOff(2, GuestOfficeId)]));
    }

    [Fact]
    public void FilterOffices_UnmatchedAipOffice_CannotBeClaimedByAGuestOffice()
    {
        // A row the V18-32 backfill could not match belongs to nobody. It must not fall to whoever
        // asks — that would hand one office another's data on the strength of a null.
        AipReadScope scope = AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: null));

        Assert.Empty(scope.FilterOffices([AipOff(3, null)]));
    }

    // ── Division axis: guest offices (PPDO-134) ───────────────────────────────

    [Fact]
    public void DivisionNarrows_IsFalseForAGuestOfficeCallerWithNoDivision()
    {
        // Most guest offices have no divisions configured yet (PPDO-122's thread). A filter keyed
        // to nothing must not read as "nothing to show".
        AipReadScope scope = AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: null));

        Assert.False(scope.DivisionNarrows);
        Assert.Null(scope.OfficeIdForAssignments);
    }

    [Fact]
    public void DivisionNarrows_IsTrueForAGuestOfficeCallerWithADivision()
    {
        // ⚠️ Before PPDO-134 this was unconditionally false for every guest caller — a division-
        // scoped guest encoder saw every program of their office regardless of division, once
        // PPDO-123 let guest offices assign divisions at all. That was the leak.
        AipReadScope scope = AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: PlanningDivId));

        Assert.True(scope.DivisionNarrows);
        Assert.Equal(GuestOfficeId, scope.OfficeIdForAssignments);
    }

    [Fact]
    public void FilterPrograms_GuestOfficeCallerWithNoDivision_KeepsEveryProgramOfItsOwnOffice()
    {
        AipReadScope scope = AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: null));
        List<AipOffice> inScope = [AipOff(2, GuestOfficeId)];
        List<AipProgram> programs = [Prog(1, 2, "P-A"), Prog(2, 2, "P-B")];

        // No assignments passed, because none are loaded when DivisionNarrows is false. If the
        // division axis leaked in here, this would come back empty and the office would report
        // missing data.
        Assert.Equal(2, scope.FilterPrograms(programs, inScope, []).Count);
    }

    [Fact]
    public void FilterPrograms_GuestOfficeCallerWithADivision_SeesOnlyThatDivisionsPrograms()
    {
        AipReadScope scope = AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: PlanningDivId));
        List<AipOffice> inScope = [AipOff(2, GuestOfficeId)];
        List<AipProgram> programs = [Prog(1, 2, "P-PLAN"), Prog(2, 2, "P-RMED")];
        List<ProgramDivision> assignments =
        [
            Assign("P-PLAN", PlanningDivId, GuestOfficeId),
            Assign("P-RMED", RmedDivId, GuestOfficeId),
        ];

        IReadOnlyList<AipProgram> result = scope.FilterPrograms(programs, inScope, assignments);

        Assert.Equal("P-PLAN", Assert.Single(result).RefCode);
    }

    [Fact]
    public void FilterPrograms_GuestOfficeCallerWithADivision_DoesNotTouchAnotherOfficesPrograms()
    {
        // A guest office's own division split must not reach into any other office's programs —
        // it only ever narrows the caller's own office.
        AipReadScope scope = AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: PlanningDivId));
        List<AipOffice> inScope = [AipOff(2, GuestOfficeId), AipOff(3, 99)];
        List<AipProgram> programs = [Prog(1, 2, "P-PLAN"), Prog(2, 2, "P-RMED"), Prog(3, 3, "OTHER-ONE")];
        List<ProgramDivision> assignments = [Assign("P-PLAN", PlanningDivId, GuestOfficeId)];

        IReadOnlyList<AipProgram> result = scope.FilterPrograms(programs, inScope, assignments);

        Assert.Equal(["OTHER-ONE", "P-PLAN"], result.Select(p => p.RefCode).OrderBy(c => c).ToArray());
    }

    // ── Division axis: host office ────────────────────────────────────────────

    [Fact]
    public void FilterPrograms_HostCallerInOneDivision_SeesOnlyThatDivisionsHostPrograms()
    {
        AipReadScope scope = AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: PlanningDivId));
        List<AipOffice> inScope = [AipOff(1, HostOfficeId)];
        List<AipProgram> programs = [Prog(1, 1, "P-PLAN"), Prog(2, 1, "P-RMED")];
        List<ProgramDivision> assignments = [Assign("P-PLAN", PlanningDivId), Assign("P-RMED", RmedDivId)];

        IReadOnlyList<AipProgram> result = scope.FilterPrograms(programs, inScope, assignments);

        Assert.Equal("P-PLAN", Assert.Single(result).RefCode);
    }

    [Fact]
    public void FilterPrograms_HostCallerInOneDivision_StillSeesGuestOfficesInFull()
    {
        // ⚠️ The subtlety. A division belongs to an office, so it can only narrow that office's
        // work. PPDO's internal division of labour says nothing about GSO's programs, and PPDO
        // reviews every office — so the guest office's programs are untouched.
        AipReadScope scope = AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: PlanningDivId));
        List<AipOffice> inScope = [AipOff(1, HostOfficeId), AipOff(2, GuestOfficeId)];
        List<AipProgram> programs = [Prog(1, 1, "P-PLAN"), Prog(2, 1, "P-RMED"), Prog(3, 2, "G-ONE")];
        List<ProgramDivision> assignments = [Assign("P-PLAN", PlanningDivId), Assign("P-RMED", RmedDivId)];

        IReadOnlyList<AipProgram> result = scope.FilterPrograms(programs, inScope, assignments);

        Assert.Equal(["P-PLAN", "G-ONE"], result.Select(p => p.RefCode).OrderByDescending(c => c).ToArray());
    }

    [Fact]
    public void FilterPrograms_HostAdminSeeingAllDivisions_IsNotNarrowed()
    {
        AipReadScope scope = AipReadScope.Resolve(
            Caller(HostOfficeId, isHost: true, divisionId: null, role: UserRole.Admin));

        Assert.False(scope.DivisionNarrows);
        List<AipProgram> programs = [Prog(1, 1, "P-PLAN"), Prog(2, 1, "P-RMED")];
        Assert.Equal(2, scope.FilterPrograms(programs, [AipOff(1, HostOfficeId)], []).Count);
    }

    [Fact]
    public void FilterPrograms_GuestAdminSeeingAllDivisions_IsNotNarrowed()
    {
        // An office's own administrator (a department head) oversees every division of its work,
        // same as a PPDO admin — never narrowed by division either.
        AipReadScope scope = AipReadScope.Resolve(
            Caller(GuestOfficeId, isHost: false, divisionId: null, role: UserRole.Admin));

        Assert.False(scope.DivisionNarrows);
        List<AipProgram> programs = [Prog(1, 2, "P-A"), Prog(2, 2, "P-B")];
        Assert.Equal(2, scope.FilterPrograms(programs, [AipOff(2, GuestOfficeId)], []).Count);
    }

    [Fact]
    public void FilterPrograms_HostStaffWithNoDivision_SeesNoneOfTheHostsOwnPrograms()
    {
        // Unassigned means unassigned on both axes since DECISION F — an empty result, never
        // "all divisions". This is the HOST-only half of the asymmetry PPDO-134 introduced; see
        // FilterPrograms_GuestOfficeCallerWithNoDivision_KeepsEveryProgramOfItsOwnOffice for the
        // opposite guest-office rule and why it differs.
        AipReadScope scope = AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: null));
        List<AipProgram> programs = [Prog(1, 1, "P-PLAN"), Prog(3, 2, "G-ONE")];

        IReadOnlyList<AipProgram> result = scope.FilterPrograms(
            programs, [AipOff(1, HostOfficeId), AipOff(2, GuestOfficeId)], []);

        Assert.Equal("G-ONE", Assert.Single(result).RefCode);
    }

    [Fact]
    public void FilterPrograms_UnassignedHostProgram_IsExcluded_NotSharedIntoEveryDivision()
    {
        // Spreading it would make each division's figures overlap. The allocation-setup panel's
        // "unassigned" count is where these are meant to be noticed.
        AipReadScope scope = AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: PlanningDivId));
        List<AipProgram> programs = [Prog(1, 1, "P-PLAN"), Prog(2, 1, "P-ORPHAN")];

        IReadOnlyList<AipProgram> result = scope.FilterPrograms(
            programs, [AipOff(1, HostOfficeId)], [Assign("P-PLAN", PlanningDivId)]);

        Assert.Equal("P-PLAN", Assert.Single(result).RefCode);
    }

    [Fact]
    public void FilterPrograms_ProgramRefCodeMatchIsCaseInsensitive()
    {
        AipReadScope scope = AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: PlanningDivId));

        IReadOnlyList<AipProgram> result = scope.FilterPrograms(
            [Prog(1, 1, "p-plan")], [AipOff(1, HostOfficeId)], [Assign("P-PLAN", PlanningDivId)]);

        Assert.Single(result);
    }

    [Fact]
    public void OfficeIdForAssignments_IsSetOnlyWhenTheDivisionAxisWillBeUsed()
    {
        // Guards against issuing the ProgramDivision query for callers who cannot be narrowed by it.
        Assert.Equal(HostOfficeId,
            AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: PlanningDivId))
                .OfficeIdForAssignments);
        Assert.Equal(GuestOfficeId,
            AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: PlanningDivId))
                .OfficeIdForAssignments);
        Assert.Null(
            AipReadScope.Resolve(Caller(HostOfficeId, isHost: true, divisionId: null, role: UserRole.Admin))
                .OfficeIdForAssignments);
        Assert.Null(
            AipReadScope.Resolve(Caller(GuestOfficeId, isHost: false, divisionId: null))
                .OfficeIdForAssignments);
    }
}
