using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="BudgetPlanningScope"/> (v1.8.0 — RAL-250).
///
/// The rule under test: division is a scoping axis ONLY for host-office (PPDO) callers.
/// PPDO separates its AIP and WFP work by division; every other office sees all of its own work
/// regardless of which division the user sits in.
///
/// The case most worth pinning is the guest-office user who HAS a division id — the one a
/// two-axis filter copied from WFP would wrongly narrow.
///
/// No mocks — BudgetPlanningScope is pure logic.
/// </summary>
public sealed class BudgetPlanningScopeTests
{
    private static Office HostOffice  => new() { Id = 1,  OfficeCode = "PPDO", IsHostOffice = true };
    private static Office GuestOffice => new() { Id = 42, OfficeCode = "GSO",  IsHostOffice = false };

    private static User MakeUser(UserRole role, Office? office, int? divisionId = null)
        => new()
        {
            Id         = Guid.NewGuid(),
            Role       = role,
            OfficeId   = office?.Id,
            Office     = office,
            DivisionId = divisionId,
        };

    // ── Host office (PPDO): division participates ─────────────────────────────

    [Fact]
    public void Resolve_HostOfficeStaff_ScopesToTheirDivision()
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(UserRole.Staff, HostOffice, divisionId: 5));

        Assert.True(scope.DivisionIsScopingAxis);
        Assert.True(scope.Office.SeeAll);
        Assert.Equal(5, scope.Division.DivisionId);
        Assert.False(scope.Division.SeeAll);
    }

    /// <summary>
    /// DivisionScope's null rule survives intact for PPDO callers: unassigned sees nothing,
    /// never "all divisions".
    /// </summary>
    [Fact]
    public void Resolve_HostOfficeStaff_WithNoDivision_SeesNothing()
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(UserRole.Staff, HostOffice));

        Assert.True(scope.DivisionIsScopingAxis);
        Assert.True(scope.Division.SeeNothing);
        Assert.False(scope.Division.SeeAll);
    }

    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    public void Resolve_HostOfficeAdmin_SeesEveryDivision(UserRole role)
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(role, HostOffice, divisionId: 5));

        Assert.True(scope.Office.SeeAll);
        Assert.True(scope.Division.SeeAll);

        // SeeAll here comes from the ADMIN bypass, not from the guest-office rule — the two
        // reasons for "no division filter" are different and DivisionIsScopingAxis separates them.
        Assert.True(scope.DivisionIsScopingAxis);
    }

    // ── Guest office: division does NOT participate ───────────────────────────

    /// <summary>
    /// The case this ticket exists for. A guest-office user legitimately carries a division id —
    /// divisions are office-scoped — and it must not narrow what they see. A two-axis filter
    /// copied from WFP would fail exactly here.
    /// </summary>
    [Fact]
    public void Resolve_GuestOfficeStaff_WithDivision_DivisionDoesNotNarrow()
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(UserRole.Staff, GuestOffice, divisionId: 5));

        Assert.False(scope.DivisionIsScopingAxis);
        Assert.True(scope.Division.SeeAll);
        Assert.Null(scope.Division.DivisionId);
    }

    [Fact]
    public void Resolve_GuestOfficeStaff_WithNoDivision_DivisionStillDoesNotNarrow()
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(UserRole.Staff, GuestOffice));

        Assert.False(scope.DivisionIsScopingAxis);
        Assert.True(scope.Division.SeeAll);
        Assert.False(scope.Division.SeeNothing);
    }

    /// <summary>
    /// The other half of the pair, and the reason Division.SeeAll is safe above: the office
    /// filter still pins a guest-office caller to one office. Consuming Division without
    /// Office is what would turn this into a see-everything scope.
    /// </summary>
    [Fact]
    public void Resolve_GuestOfficeStaff_IsStillBoundedByTheOfficeAxis()
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(UserRole.Staff, GuestOffice, divisionId: 5));

        Assert.False(scope.Office.SeeAll);
        Assert.Equal(42, scope.Office.OfficeId);
    }

    /// <summary>Office wins over role here too — an admin tied to a guest office stays scoped.</summary>
    [Theory]
    [InlineData(UserRole.SuperAdmin)]
    [InlineData(UserRole.Admin)]
    public void Resolve_AdminInAGuestOffice_IsStillScopedToThatOffice(UserRole role)
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(role, GuestOffice, divisionId: 5));

        Assert.False(scope.Office.SeeAll);
        Assert.Equal(42, scope.Office.OfficeId);
        Assert.False(scope.DivisionIsScopingAxis);
        Assert.True(scope.Division.SeeAll);
    }

    // ── No office at all ──────────────────────────────────────────────────────

    /// <summary>
    /// Post-DECISION-F: no office means unassigned, not PPDO. The office axis matches nothing,
    /// so the division axis cannot widen it back.
    /// </summary>
    [Fact]
    public void Resolve_UserWithNoOffice_ScopesToNothing()
    {
        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(
            MakeUser(UserRole.Staff, office: null, divisionId: 5));

        Assert.False(scope.DivisionIsScopingAxis);
        Assert.False(scope.Office.SeeAll);
        Assert.Equal(OfficeScope.NoOffice, scope.Office.OfficeId);
    }

    /// <summary>
    /// A forgotten <c>.Include(u =&gt; u.Office)</c> must degrade to MORE restrictive, never to
    /// full access — the same guarantee OfficeScope makes, restated on the combined scope
    /// because that is where Phase 3 will read it.
    /// </summary>
    [Fact]
    public void Resolve_HostOfficeUser_WithOfficeNavigationNotLoaded_IsTreatedAsGuest()
    {
        User user = new()
        {
            Id         = Guid.NewGuid(),
            Role       = UserRole.Staff,
            OfficeId   = 1,      // the host office...
            Office     = null,   // ...but the navigation was never included
            DivisionId = 5,
        };

        BudgetPlanningScope scope = BudgetPlanningScope.Resolve(user);

        Assert.False(scope.DivisionIsScopingAxis);
        Assert.False(scope.Office.SeeAll);
        Assert.Equal(1, scope.Office.OfficeId);
    }
}
