using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="OfficeScope"/> (v1.8.0 — RAL-228).
///
///   PPDO-internal caller (OfficeId null) → All  (no office filter)
///   Office user        (OfficeId set)    → For(theirOfficeId)
///
/// ⚠️ Deliberately asymmetric with <see cref="DivisionScope"/>: there is no "SeeNothing"
/// state here. A null OfficeId means PPDO-internal (full access), NOT "unassigned".
/// These tests pin that asymmetry so it cannot be "fixed" into a security inversion.
///
/// No mocks — OfficeScope is pure logic.
/// </summary>
public sealed class OfficeScopeTests
{
    private static User MakeUser(UserRole role, int? officeId)
        => new() { Id = Guid.NewGuid(), Role = role, OfficeId = officeId };

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    [InlineData(UserRole.Staff)]
    public void Resolve_PpdoUserWithNullOfficeId_ReturnsAll(UserRole role)
    {
        OfficeScope scope = OfficeScope.Resolve(MakeUser(role, officeId: null));

        Assert.True(scope.SeeAll);
        Assert.Null(scope.OfficeId);
    }

    [Fact]
    public void Resolve_OfficeUser_ReturnsScopeForTheirOffice()
    {
        OfficeScope scope = OfficeScope.Resolve(MakeUser(UserRole.Staff, officeId: 42));

        Assert.False(scope.SeeAll);
        Assert.Equal(42, scope.OfficeId);
    }

    /// <summary>
    /// Judgement call pinned by test (RAL-228): office wins over role. OfficeId is the data-scoping
    /// dimension; the SuperAdmin/Admin bypass in PermissionService governs FEATURE flags, not data
    /// scope. An admin account deliberately tied to an office is scoped to that office.
    /// </summary>
    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    public void Resolve_AdminOrAboveWithOfficeIdSet_IsStillScopedToThatOffice(UserRole role)
    {
        OfficeScope scope = OfficeScope.Resolve(MakeUser(role, officeId: 7));

        Assert.False(scope.SeeAll);
        Assert.Equal(7, scope.OfficeId);
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
    public void Clamp_PpdoUser_PassesTheRequestedOfficeThrough()
    {
        Assert.Equal(99, OfficeScope.All.Clamp(requestedOfficeId: 99));
    }

    [Fact]
    public void Clamp_PpdoUserRequestingNothing_ReturnsNull()
    {
        Assert.Null(OfficeScope.All.Clamp(requestedOfficeId: null));
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
    public void Permits_PpdoUser_AnyOffice_IsTrue()
    {
        Assert.True(OfficeScope.All.Permits(1));
        Assert.True(OfficeScope.All.Permits(99));
    }

    /// <summary>
    /// A record with no owning office (e.g. LDIP's multi-office bulk uploads, where
    /// <c>LdipRecord.OfficeId</c> is null) is PPDO-only — an office user must not reach it.
    /// </summary>
    [Fact]
    public void Permits_UnownedRecord_OnlyPpdoMayRead()
    {
        Assert.True(OfficeScope.All.Permits(owningOfficeId: null));
        Assert.False(OfficeScope.For(42).Permits(owningOfficeId: null));
    }
}
