using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="OfficeScope"/> (RAL-228; discriminator changed by DECISION F, RAL-258).
///
///   Host-office user (Office.IsHostOffice) → All  (no office filter)
///   Any other office user                  → For(theirOfficeId)
///   No office at all                       → scoped to nothing
///
/// ⚠️ <b>The rule that changed.</b> A null <c>OfficeId</c> used to mean "PPDO-internal, sees
/// everything" — the inverse of <see cref="DivisionScope"/>, where null means "unassigned, sees
/// nothing". Cross-office authority now comes from the office row's flag, so null is free to mean
/// what it means everywhere else: unassigned, and therefore scoped to nothing. These tests pin the
/// new direction so the old inversion cannot be reintroduced.
///
/// The rule that did <b>not</b> change: office wins over role. An admin tied to an office stays
/// scoped to it.
///
/// No mocks — OfficeScope is pure logic.
/// </summary>
public sealed class OfficeScopeTests
{
    private static Office HostOffice => new() { Id = 1, OfficeCode = "PPDO", IsHostOffice = true };
    private static Office GuestOffice => new() { Id = 42, OfficeCode = "GSO", IsHostOffice = false };

    private static User MakeUser(UserRole role, Office? office)
        => new() { Id = Guid.NewGuid(), Role = role, OfficeId = office?.Id, Office = office };

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public void Resolve_HostOfficeUser_ReturnsAll(UserRole role)
    {
        OfficeScope scope = OfficeScope.Resolve(MakeUser(role, HostOffice));

        Assert.True(scope.SeeAll);
        Assert.Null(scope.OfficeId);
    }

    [Fact]
    public void Resolve_NonHostOfficeUser_ReturnsScopeForTheirOffice()
    {
        OfficeScope scope = OfficeScope.Resolve(MakeUser(UserRole.Staff, GuestOffice));

        Assert.False(scope.SeeAll);
        Assert.Equal(42, scope.OfficeId);
    }

    /// <summary>
    /// Judgement call pinned by test (RAL-228), unchanged by DECISION F: office wins over role.
    /// The SuperAdmin/Admin bypass in PermissionService governs FEATURE flags, not data scope, so
    /// an admin account deliberately tied to a non-host office stays scoped to that office.
    /// </summary>
    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    public void Resolve_AdminOrAboveInANonHostOffice_IsStillScopedToThatOffice(UserRole role)
    {
        OfficeScope scope = OfficeScope.Resolve(MakeUser(role, GuestOffice));

        Assert.False(scope.SeeAll);
        Assert.Equal(42, scope.OfficeId);
    }

    /// <summary>The other half of the same rule: the flag grants the bypass, not the role.</summary>
    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Staff)]
    public void Resolve_HostOfficeGrantsTheBypassRegardlessOfRole(UserRole role)
    {
        Assert.True(OfficeScope.Resolve(MakeUser(role, HostOffice)).SeeAll);
    }

    /// <summary>
    /// The inversion this change removes. Before DECISION F a user with no office saw EVERY
    /// office; now they see none. A record in this state is incomplete, not privileged.
    /// </summary>
    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public void Resolve_UserWithNoOffice_SeesNothing(UserRole role)
    {
        OfficeScope scope = OfficeScope.Resolve(MakeUser(role, office: null));

        Assert.False(scope.SeeAll);
        Assert.Equal(OfficeScope.NoOffice, scope.OfficeId);
        Assert.False(scope.Permits(1));
        Assert.False(scope.Permits(null));
    }

    /// <summary>
    /// The flag lives on the navigation property, so a query that forgets
    /// <c>.Include(u =&gt; u.Office)</c> cannot see it. That must degrade to MORE restrictive
    /// (scoped to their own office), never to full access.
    /// </summary>
    [Fact]
    public void Resolve_OfficeNavigationNotLoaded_FailsClosedToTheirOwnOffice()
    {
        User user = new() { Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = 1, Office = null };

        OfficeScope scope = OfficeScope.Resolve(user);

        Assert.False(scope.SeeAll);
        Assert.Equal(1, scope.OfficeId);
    }

    [Fact]
    public void All_HasNoOfficeFilter()
    {
        Assert.True(OfficeScope.All.SeeAll);
        Assert.Null(OfficeScope.All.OfficeId);
    }

    [Fact]
    public void For_ScopesToTheGivenOffice()
    {
        OfficeScope scope = OfficeScope.For(3);

        Assert.False(scope.SeeAll);
        Assert.Equal(3, scope.OfficeId);
    }

    // ── Clamp ─────────────────────────────────────────────────────────────────
    // Clamp, never reject: an office user's requested id is silently replaced with their own,
    // so there is no error path to get wrong and no way to probe for other offices' ids.

    [Fact]
    public void Clamp_OfficeUserRequestingAnotherOffice_ReturnsTheirOwnOffice()
    {
        Assert.Equal(42, OfficeScope.For(42).Clamp(requestedOfficeId: 99));
    }

    [Fact]
    public void Clamp_OfficeUserRequestingTheirOwnOffice_ReturnsThatOffice()
    {
        Assert.Equal(42, OfficeScope.For(42).Clamp(requestedOfficeId: 42));
    }

    [Fact]
    public void Clamp_OfficeUserRequestingNothing_ReturnsTheirOwnOffice()
    {
        Assert.Equal(42, OfficeScope.For(42).Clamp(requestedOfficeId: null));
    }

    [Fact]
    public void Clamp_HostOfficeUser_PassesTheRequestedOfficeThrough()
    {
        Assert.Equal(99, OfficeScope.All.Clamp(requestedOfficeId: 99));
    }

    [Fact]
    public void Clamp_HostOfficeUserRequestingNothing_ReturnsNull()
    {
        Assert.Null(OfficeScope.All.Clamp(requestedOfficeId: null));
    }

    /// <summary>
    /// The dangerous case: clamping for an office-less user must not return null, because callers
    /// read null as "no filter — every office".
    /// </summary>
    [Fact]
    public void Clamp_UserWithNoOffice_NeverReturnsNull()
    {
        OfficeScope scope = OfficeScope.Resolve(
            new User { Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = null, Office = null });

        Assert.Equal(OfficeScope.NoOffice, scope.Clamp(requestedOfficeId: null));
        Assert.Equal(OfficeScope.NoOffice, scope.Clamp(requestedOfficeId: 99));
    }

    // ── Permits ───────────────────────────────────────────────────────────────
    // For read guards on a record whose owning office is already known.

    [Fact]
    public void Permits_OfficeUser_OwnOffice_IsTrue()
    {
        Assert.True(OfficeScope.For(42).Permits(42));
    }

    [Fact]
    public void Permits_OfficeUser_ForeignOffice_IsFalse()
    {
        Assert.False(OfficeScope.For(42).Permits(99));
    }

    [Fact]
    public void Permits_HostOfficeUser_AnyOffice_IsTrue()
    {
        Assert.True(OfficeScope.All.Permits(1));
        Assert.True(OfficeScope.All.Permits(99));
    }

    /// <summary>
    /// A record with no owning office (e.g. LDIP's multi-office bulk uploads, where
    /// <c>LdipRecord.OfficeId</c> is null) is host-office-only — a guest office must not reach it.
    /// Note this is a record-level null, a different concept from a user's null office.
    /// </summary>
    [Fact]
    public void Permits_UnownedRecord_OnlyTheHostOfficeMayRead()
    {
        Assert.True(OfficeScope.All.Permits(owningOfficeId: null));
        Assert.False(OfficeScope.For(42).Permits(owningOfficeId: null));
    }
}
