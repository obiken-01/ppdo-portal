using PPDO.Application.Common;
using PPDO.Domain.Enums;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="LandingPageRoutes"/> (v1.8.0 — RAL-261).
///
/// These pin the enum → route mapping. The routes mirror directories under
/// <c>frontend/src/app/(portal)/</c>; if one is renamed there without updating the mapper,
/// the exact-value assertions below fail rather than users being redirected into a 404.
/// </summary>
public sealed class LandingPageRoutesTests
{
    [Theory]
    [InlineData(LandingPage.MainDashboard,           "/dashboard")]
    [InlineData(LandingPage.InventoryDashboard,      "/inventory")]
    [InlineData(LandingPage.BudgetPlanningDashboard, "/budget-planning")]
    [InlineData(LandingPage.Profile,                 "/account")]
    public void PathFor_KnownPage_ReturnsItsRoute(LandingPage page, string expected)
    {
        Assert.Equal(expected, LandingPageRoutes.PathFor(page));
    }

    [Fact]
    public void PathFor_EveryEnumMember_HasARoute()
    {
        // A new member added without a mapping would silently fall to the default branch.
        foreach (LandingPage page in Enum.GetValues<LandingPage>())
        {
            string path = LandingPageRoutes.PathFor(page);

            Assert.StartsWith("/", path);
            if (page is not LandingPage.Profile)
                Assert.True(path != LandingPageRoutes.Fallback,
                    $"{page} has no route of its own and fell through to the fallback.");
        }
    }

    [Fact]
    public void PathFor_UnknownValue_FallsBackToAccount()
    {
        Assert.Equal("/account", LandingPageRoutes.PathFor((LandingPage)999));
    }
}
