using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="WfpDivisionScope"/> (v1.8.3 — PPDO-198): the division axis of the
/// WFP surface, inside the host office.
///
///   SuperAdmin / Admin / CanManagePpdoAllocation → every division
///   Staff with a division                        → that division only
///   Staff with no division                       → nothing (never "all divisions")
///
/// The office axis is not here: guest offices are refused earlier by
/// <c>IPermissionService.CanAccessWfpAsync</c>. No mocks — the type is pure logic.
/// </summary>
public sealed class WfpDivisionScopeTests
{
    private const int OwnDivision   = 3;
    private const int OtherDivision = 4;

    private static User MakeUser(UserRole role, int? divisionId)
        => new() { Id = Guid.NewGuid(), Role = role, DivisionId = divisionId };

    // ── Resolve ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    public void Resolve_AdminOrAbove_SeesEveryDivision(UserRole role)
    {
        DivisionScope scope = WfpDivisionScope.Resolve(MakeUser(role, OwnDivision), canManagePpdoAllocation: false);

        Assert.True(scope.SeeAll);
    }

    [Fact]
    public void Resolve_FinanceStaff_SeesEveryDivision()
    {
        DivisionScope scope = WfpDivisionScope.Resolve(MakeUser(UserRole.Staff, OwnDivision), canManagePpdoAllocation: true);

        Assert.True(scope.SeeAll);
    }

    [Fact]
    public void Resolve_Staff_IsScopedToTheirOwnDivision()
    {
        DivisionScope scope = WfpDivisionScope.Resolve(MakeUser(UserRole.Staff, OwnDivision), canManagePpdoAllocation: false);

        Assert.False(scope.SeeAll);
        Assert.Equal(OwnDivision, scope.DivisionId);
    }

    [Fact]
    public void Resolve_StaffWithNoDivision_SeesNothing()
    {
        // ⚠️ The case the old handler helper got wrong: it returned caller.DivisionId, and a null
        // there meant "no filter" — every division's WFP.
        DivisionScope scope = WfpDivisionScope.Resolve(MakeUser(UserRole.Staff, null), canManagePpdoAllocation: false);

        Assert.True(scope.SeeNothing);
        Assert.False(scope.SeeAll);
    }

    // ── Allows ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(OwnDivision,   true)]
    [InlineData(OtherDivision, false)]
    [InlineData(null,          false)]   // an office-level (division-less) WFP is not a Staff user's
    public void Allows_DivisionScopedStaff(int? recordDivisionId, bool expected)
        => Assert.Equal(expected, WfpDivisionScope.Allows(DivisionScope.For(OwnDivision), recordDivisionId));

    [Theory]
    [InlineData(OwnDivision)]
    [InlineData(OtherDivision)]
    [InlineData(null)]
    public void Allows_SeeAll_AllowsEveryRecord(int? recordDivisionId)
        => Assert.True(WfpDivisionScope.Allows(DivisionScope.All, recordDivisionId));

    [Theory]
    [InlineData(OwnDivision)]
    [InlineData(null)]
    public void Allows_SeeNothing_AllowsNoRecord(int? recordDivisionId)
        => Assert.False(WfpDivisionScope.Allows(DivisionScope.Nothing, recordDivisionId));

    // ── TryFilter ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData(OtherDivision)]
    public void TryFilter_SeeAll_PassesTheRequestThrough(int? requested)
    {
        Assert.True(WfpDivisionScope.TryFilter(DivisionScope.All, requested, out int? divisionId));
        Assert.Equal(requested, divisionId);
    }

    [Theory]
    [InlineData(null)]           // asking for "every division"
    [InlineData(OtherDivision)]  // asking for someone else's
    public void TryFilter_DivisionScoped_IsPinnedToOwnDivision(int? requested)
    {
        Assert.True(WfpDivisionScope.TryFilter(DivisionScope.For(OwnDivision), requested, out int? divisionId));
        Assert.Equal(OwnDivision, divisionId);
    }

    [Fact]
    public void TryFilter_Nothing_RefusesTheQuery()
        => Assert.False(WfpDivisionScope.TryFilter(DivisionScope.Nothing, null, out _));
}
