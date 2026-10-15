using Moq;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Who each notification reaches (v1.9.0 — PPDO-221, <c>Notification_Centre_Spec.md</c> §3.1, §11).
/// Real <see cref="PermissionService"/>; the user query is a fake that applies the same superset
/// predicate as <c>UserRepository.GetNotificationCandidatesAsync</c>, so these tests pin the in-memory
/// rules and the SQL predicate is checked against the local database (see the PR).
///
/// <para>
/// One cast of users across three offices. Every "excluded" assertion names the rule that excludes
/// the user, because a scope leak here sends one office's comment text to another office's bell.
/// </para>
/// </summary>
public sealed class NotificationAudienceTests
{
    private const int Ppdo = 1, Pto = 2, Gso = 3;
    private const int PtoDivA = 21, PtoDivB = 22, PpdoPlanning = 11;
    private const string ProgramA = "1000-000-1-01-001";   // assigned to PTO division A
    private const string ProgramB = "1000-000-1-01-002";   // assigned to PTO division B

    private static readonly Office PpdoOffice = MakeOffice(Ppdo, "PPDO", host: true);
    private static readonly Office PtoOffice  = MakeOffice(Pto, "PTO", host: false);
    private static readonly Office GsoOffice  = MakeOffice(Gso, "GSO", host: false);

    // PTO, the office whose work is the target.
    private readonly User _ptoEncoderA     = Make("PTO encoder A", PtoOffice, PtoDivA);
    private readonly User _ptoEncoderB     = Make("PTO encoder B", PtoOffice, PtoDivB);
    private readonly User _ptoUnassigned   = Make("PTO no division", PtoOffice, null);
    private readonly User _ptoDeptHead     = Make("PTO head", PtoOffice, null, u => u.OverrideCanReviewBudgetPlanning = true);
    private readonly User _ptoAdmin        = Make("PTO admin", PtoOffice, null, role: UserRole.Admin);
    private readonly User _ptoNoBudget     = Make("PTO no BP", PtoOffice, PtoDivA, u => u.OverrideCanAccessBudgetPlanning = false);

    // PPDO, the host office.
    private readonly User _ppdoReviewer    = Make("PPDO reviewer", PpdoOffice, PpdoPlanning, u => u.OverrideCanReviewAllOffices = true);
    private readonly User _ppdoStaff       = Make("PPDO staff", PpdoOffice, PpdoPlanning);
    private readonly User _ppdoUnassigned  = Make("PPDO no division", PpdoOffice, null, u => u.OverrideCanAccessBudgetPlanning = true);
    private readonly User _ppdoHeadOfOwn   = Make("PPDO head", PpdoOffice, PpdoPlanning, u => u.OverrideCanReviewBudgetPlanning = true);

    // Elsewhere.
    private readonly User _gsoEncoder      = Make("GSO encoder", GsoOffice, null);
    private readonly User _gsoReviewer     = Make("GSO-based reviewer", GsoOffice, null, u => u.OverrideCanReviewAllOffices = true);
    private readonly User _superAdmin      = Make("Support", null, null, role: UserRole.SuperAdmin);

    private readonly Mock<IUserRepository>       _users      = new();
    private readonly Mock<IAllocationRepository> _allocation = new();
    private readonly NotificationAudience        _sut;

    private readonly AipOffice  _ptoAipOffice = new() { Id = 200, AipRecordId = 100, OfficeId = Pto, RefCode = "1000-000-1-01-200", Name = "PTO", Sector = "General" };
    private readonly AipProgram _programA;
    private readonly AipProgram _programB;

    public NotificationAudienceTests()
    {
        _programA = new AipProgram { Id = 300, OfficeId = _ptoAipOffice.Id, RefCode = ProgramA, Name = "Program A" };
        _programB = new AipProgram { Id = 301, OfficeId = _ptoAipOffice.Id, RefCode = ProgramB, Name = "Program B" };

        List<User> everyone =
        [
            _ptoEncoderA, _ptoEncoderB, _ptoUnassigned, _ptoDeptHead, _ptoAdmin, _ptoNoBudget,
            _ppdoReviewer, _ppdoStaff, _ppdoUnassigned, _ppdoHeadOfOwn, _gsoEncoder, _gsoReviewer, _superAdmin,
        ];

        // The repository's superset predicate, in memory.
        _users.Setup(r => r.GetNotificationCandidatesAsync(It.IsAny<int?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int? officeId, bool cross, CancellationToken _) => (IReadOnlyList<User>)everyone
                .Where(u => (officeId is not null && u.OfficeId == officeId)
                         || (cross && (u.Office?.IsHostOffice == true || u.Role == UserRole.SuperAdmin
                                       || u.OverrideCanReviewAllOffices == true)))
                .ToList());

        _allocation.Setup(r => r.GetProgramDivisionsByOfficeIdAsync(Pto, It.IsAny<CancellationToken>()))
            .ReturnsAsync(
            [
                new ProgramDivision { OfficeId = Pto, OfficeRefCode = "01-200", ProgramRefCode = ProgramA, DivisionId = PtoDivA },
                new ProgramDivision { OfficeId = Pto, OfficeRefCode = "01-200", ProgramRefCode = ProgramB, DivisionId = PtoDivB },
            ]);

        _sut = new NotificationAudience(_users.Object, _allocation.Object, new PermissionService());
    }

    // ── Proposal readers ──────────────────────────────────────────────────────

    [Fact]
    public async Task ProposalReaders_AreEveryoneWhoCanOpenIt_DivisionAxisIncluded()
    {
        IReadOnlyList<User> readers = await _sut.ProposalReadersAsync(_ptoAipOffice, _programA);

        AssertExactly(readers,
            _ptoEncoderA,                    // their division's program
            _ptoUnassigned,                  // a guest office's unassigned Staff is not narrowed (AipReadScope)
            _ptoDeptHead, _ptoAdmin,         // not narrowed
            _ppdoReviewer, _ppdoStaff, _ppdoHeadOfOwn, // host office reads every office; PPDO divisions never narrow a guest office
            _ppdoUnassigned,                 // ...not even "no division": that only hides PPDO's OWN programs (AipReadScope)
            _gsoReviewer,                    // cross-office review scope, wherever they sit
            _superAdmin);
        // Excluded: _ptoEncoderB (division B, program A — the division axis); _ptoNoBudget (no Budget
        // Planning); _gsoEncoder (another guest office).
    }

    [Fact]
    public async Task ProposalReaders_OnAnotherDivisionsProgram_SwapsTheEncoders()
    {
        IReadOnlyList<User> readers = await _sut.ProposalReadersAsync(_ptoAipOffice, _programB);

        Assert.Contains(_ptoEncoderB, readers);
        Assert.DoesNotContain(_ptoEncoderA, readers);
        Assert.Contains(_ptoUnassigned, readers);
    }

    [Fact]
    public async Task ProposalReaders_NeverAnotherGuestOfficesUser()
    {
        Assert.DoesNotContain(_gsoEncoder, await _sut.ProposalReadersAsync(_ptoAipOffice, _programA));
    }

    // ── Activity readers (reviewer edit) ──────────────────────────────────────

    [Fact]
    public async Task ActivityReaders_AreTheOfficesOwnUsersOnly_DivisionAxisIncluded()
    {
        IReadOnlyList<User> readers = await _sut.ActivityReadersAsync(_ptoAipOffice, _programA);

        AssertExactly(readers, _ptoEncoderA, _ptoUnassigned, _ptoDeptHead, _ptoAdmin);
        // Never PPDO's reviewers, who made the edit, nor any other office.
    }

    [Fact]
    public async Task ActivityReaders_AskOnlyForTheOfficesOwnUsers()
    {
        await _sut.ActivityReadersAsync(_ptoAipOffice, _programA);

        _users.Verify(r => r.GetNotificationCandidatesAsync(Pto, false, It.IsAny<CancellationToken>()), Times.Once);
        _users.VerifyNoOtherCalls();
    }

    // ── Office AIP readers (returns) ──────────────────────────────────────────

    [Fact]
    public async Task OfficeAipReaders_GuestOffice_EveryUserWithBudgetPlanning()
    {
        AssertExactly(await _sut.OfficeAipReadersAsync(Pto),
            _ptoEncoderA, _ptoEncoderB, _ptoUnassigned, _ptoDeptHead, _ptoAdmin);
    }

    [Fact]
    public async Task OfficeAipReaders_HostOffice_DropsStaffWithNoDivision()
    {
        // BudgetPlanningScope: division is a scoping axis for the host office, and unassigned sees
        // nothing — even though this user holds Budget Planning by override.
        IReadOnlyList<User> readers = await _sut.OfficeAipReadersAsync(Ppdo);

        Assert.DoesNotContain(_ppdoUnassigned, readers);
        Assert.Contains(_ppdoStaff, readers);
        Assert.DoesNotContain(_superAdmin, readers);   // no office: not one of PPDO's users
    }

    // ── Division members (department-head return) ─────────────────────────────

    [Fact]
    public async Task DivisionMembers_OnlyTheReturnedDivisionsUsers()
    {
        AssertExactly(await _sut.DivisionMembersAsync(Pto, [PtoDivA]), _ptoEncoderA);
        AssertExactly(await _sut.DivisionMembersAsync(Pto, [PtoDivA, PtoDivB]), _ptoEncoderA, _ptoEncoderB);
        Assert.Empty(await _sut.DivisionMembersAsync(Pto, []));
    }

    // ── Department heads (division submitted) ─────────────────────────────────

    [Fact]
    public async Task DepartmentHeads_AreTheOfficesOwnHeadsAndAdmins_NeverAnotherOfficesReviewer()
    {
        AssertExactly(await _sut.DepartmentHeadsAsync(Pto), _ptoDeptHead, _ptoAdmin);
        // _ppdoHeadOfOwn holds CanReviewBudgetPlanning but is PPDO's head, not PTO's (an office
        // comparison, Permission Matrix §3.2); _superAdmin resolves the grant but sits in no office.
    }

    // ── Cross-office reviewers (sent to PPDO) ─────────────────────────────────

    [Fact]
    public async Task CrossOfficeReviewers_AreTheGrantHolders_WhereverTheySit()
    {
        AssertExactly(await _sut.CrossOfficeReviewersAsync(), _ppdoReviewer, _gsoReviewer, _superAdmin);
        // Not _ppdoStaff: reading every office is not reviewing them.
    }

    // ── One query per event ───────────────────────────────────────────────────

    [Fact]
    public async Task ProposalReaders_IsOneUserQueryAndAtMostOneAssignmentQuery_NotOnePerUser()
    {
        await _sut.ProposalReadersAsync(_ptoAipOffice, _programA);

        _users.Verify(r => r.GetNotificationCandidatesAsync(Pto, true, It.IsAny<CancellationToken>()), Times.Once);
        _allocation.Verify(r => r.GetProgramDivisionsByOfficeIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AssertExactly(IReadOnlyList<User> actual, params User[] expected)
        => Assert.Equal(
            expected.Select(u => u.FullName).OrderBy(n => n),
            actual.Select(u => u.FullName).OrderBy(n => n));

    private static Office MakeOffice(int id, string code, bool host) => new()
    {
        Id = id, OfficeCode = code, OfficeName = code, IsHostOffice = host, IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User Make(
        string name, Office? office, int? divisionId, Action<User>? tweak = null, UserRole role = UserRole.Staff)
    {
        User u = new()
        {
            Id = Guid.NewGuid(), FullName = name, Username = name, PasswordHash = "h", Role = role,
            OfficeId = office?.Id, Office = office, DivisionId = divisionId,
            // A host division grants Budget Planning; a guest office's default is on regardless.
            Division = divisionId is int d
                ? new Division { Id = d, OfficeId = office?.Id ?? 0, Name = $"Div {d}", CanAccessBudgetPlanning = true }
                : null,
            IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        tweak?.Invoke(u);
        return u;
    }
}
