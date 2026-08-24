using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="LandingPageResolver"/> (v1.8.0 — RAL-251).
///
/// Chain: user preference → division default → office default → first reachable → Profile.
///
/// The property that matters most is that an **unreachable stored preference is skipped, not
/// returned**. Returning one produces a redirect loop rather than an error, so these tests pin
/// that behaviour at every level of the chain.
///
/// Uses the real <see cref="PermissionService"/> rather than a mock — the two are meant to agree
/// about what a user can reach, and a mocked permission service would let them drift apart.
/// </summary>
public sealed class LandingPageResolverTests
{
    private static LandingPageResolver Sut() => new(new PermissionService());

    private static Division DivisionWith(bool inventory = false, bool budget = false,
                                         LandingPage? landing = null)
        => new()
        {
            Id = 1,
            Name = "Test Division",
            CanAccessInventory = inventory,
            CanAccessBudgetPlanning = budget,
            LandingPage = landing,
        };

    /// <summary>PPDO-internal Staff (no office). Division drives their flags.</summary>
    private static User PpdoStaff(Division? division = null, LandingPage? landing = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Staff,
            OfficeId = null,
            Division = division,
            DivisionId = division?.Id,
            LandingPage = landing,
        };

    /// <summary>Non-PPDO office user. Budget Planning is their only feature.</summary>
    private static User OfficeUser(LandingPage? landing = null, Office? office = null)
        => new()
        {
            Id = Guid.NewGuid(),
            Role = UserRole.Staff,
            OfficeId = 7,
            Office = office,
            LandingPage = landing,
        };

    // ── Preference chain ──────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_UserPreferenceSetAndReachable_WinsOverEverything()
    {
        User user = PpdoStaff(
            DivisionWith(inventory: true, landing: LandingPage.InventoryDashboard),
            landing: LandingPage.MainDashboard);

        Assert.Equal(LandingPage.MainDashboard, await Sut().ResolveAsync(user));
    }

    [Fact]
    public async Task ResolveAsync_NoUserPreference_FallsBackToDivisionDefault()
    {
        User user = PpdoStaff(DivisionWith(inventory: true, landing: LandingPage.InventoryDashboard));

        Assert.Equal(LandingPage.InventoryDashboard, await Sut().ResolveAsync(user));
    }

    [Fact]
    public async Task ResolveAsync_NoUserOrDivisionPreference_FallsBackToOfficeDefault()
    {
        Office office = new() { Id = 7, OfficeName = "PEO", LandingPage = LandingPage.Profile };
        User user = OfficeUser(office: office);

        Assert.Equal(LandingPage.Profile, await Sut().ResolveAsync(user));
    }

    // ── Unreachable preferences are skipped, never returned ───────────────────

    [Fact]
    public async Task ResolveAsync_UserPrefersInventoryButCannotAccessIt_SkipsToNextLevel()
    {
        // Inventory access revoked after the preference was saved.
        User user = PpdoStaff(DivisionWith(inventory: false), landing: LandingPage.InventoryDashboard);

        // Falls through to the ordered list, whose first reachable entry is the main dashboard.
        Assert.Equal(LandingPage.MainDashboard, await Sut().ResolveAsync(user));
    }

    [Fact]
    public async Task ResolveAsync_OfficeUserPrefersMainDashboard_SkipsIt()
    {
        // The portal layout gate bounces office users off /dashboard — honouring this
        // preference would loop between the gate and the redirect.
        User user = OfficeUser(landing: LandingPage.MainDashboard);

        Assert.Equal(LandingPage.BudgetPlanningDashboard, await Sut().ResolveAsync(user));
    }

    [Fact]
    public async Task ResolveAsync_DivisionDefaultUnreachable_SkipsToOfficeDefault()
    {
        Office office = new() { Id = 7, OfficeName = "PEO", LandingPage = LandingPage.Profile };
        User user = OfficeUser(office: office);
        user.Division = DivisionWith(inventory: false, landing: LandingPage.InventoryDashboard);

        Assert.Equal(LandingPage.Profile, await Sut().ResolveAsync(user));
    }

    // ── Fallback order ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAsync_PpdoUserWithNothingConfigured_GetsMainDashboard()
    {
        Assert.Equal(LandingPage.MainDashboard, await Sut().ResolveAsync(PpdoStaff(DivisionWith())));
    }

    [Fact]
    public async Task ResolveAsync_OfficeUserWithNothingConfigured_GetsBudgetPlanning()
    {
        // CanAccessBudgetPlanning defaults ON for office users — it is their only feature.
        Assert.Equal(LandingPage.BudgetPlanningDashboard, await Sut().ResolveAsync(OfficeUser()));
    }

    [Fact]
    public async Task ResolveAsync_OfficeUserWithBudgetPlanningRevoked_GetsProfile()
    {
        // The exact case the existing layout gate calls out: sending this user to
        // /budget-planning would eject them and loop.
        User user = OfficeUser();
        user.OverrideCanAccessBudgetPlanning = false;

        Assert.Equal(LandingPage.Profile, await Sut().ResolveAsync(user));
    }

    [Fact]
    public async Task ResolveAsync_AlwaysReturnsSomethingReachable()
    {
        // Whatever the configuration, the result must be safe to redirect to.
        LandingPageResolver sut = Sut();
        User[] users = [
            PpdoStaff(DivisionWith()),
            PpdoStaff(DivisionWith(inventory: true), landing: LandingPage.InventoryDashboard),
            OfficeUser(landing: LandingPage.MainDashboard),
            new() { Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = 7,
                    OverrideCanAccessBudgetPlanning = false },
        ];

        foreach (User user in users)
        {
            LandingPage resolved = await sut.ResolveAsync(user);
            Assert.True(await sut.IsReachableAsync(user, resolved),
                $"Resolver returned {resolved}, which this user cannot reach.");
        }
    }

    // ── IsReachableAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task IsReachableAsync_Profile_IsAlwaysTrue()
    {
        Assert.True(await Sut().IsReachableAsync(OfficeUser(), LandingPage.Profile));
        Assert.True(await Sut().IsReachableAsync(PpdoStaff(DivisionWith()), LandingPage.Profile));
    }

    [Fact]
    public async Task IsReachableAsync_MainDashboard_IsFalseForOfficeUsers()
    {
        Assert.False(await Sut().IsReachableAsync(OfficeUser(), LandingPage.MainDashboard));
        Assert.True(await Sut().IsReachableAsync(PpdoStaff(DivisionWith()), LandingPage.MainDashboard));
    }

    [Fact]
    public async Task IsReachableAsync_SuperAdmin_CanReachEveryPage()
    {
        User superAdmin = new() { Id = Guid.NewGuid(), Role = UserRole.SuperAdmin, OfficeId = null };

        foreach (LandingPage page in Enum.GetValues<LandingPage>())
            Assert.True(await Sut().IsReachableAsync(superAdmin, page), $"{page} should be reachable");
    }
}
