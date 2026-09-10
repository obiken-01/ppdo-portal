using PPDO.Domain.Enums;

namespace PPDO.Application.Common;

/// <summary>
/// Maps a <see cref="LandingPage"/> to the frontend route it corresponds to (RAL-261).
///
/// ⚠️ These strings mirror routes under <c>frontend/src/app/(portal)/</c>. They live here — not
/// in the database — precisely so that a route rename is a one-line change in one file rather
/// than a data migration across three tables. The database stores the stable enum key; this is
/// the only place that knows what today's URL for it is.
///
/// The API resolves the path server-side so the login page, the portal layout, the sidebar and
/// the PWA cannot drift apart the way <c>APP_VERSION</c> did.
/// </summary>
public static class LandingPageRoutes
{
    /// <summary>Route every authenticated user can reach — the terminal fallback.</summary>
    public const string Fallback = "/account";

    /// <summary>Returns the portal route for <paramref name="page"/>.</summary>
    public static string PathFor(LandingPage page) => page switch
    {
        LandingPage.MainDashboard           => "/dashboard",
        LandingPage.InventoryDashboard      => "/inventory",
        LandingPage.BudgetPlanningDashboard => "/budget-planning",
        LandingPage.Profile                 => Fallback,

        // Unreachable for a resolved value, but a new enum member added without a route here
        // must land somewhere safe rather than redirect to an empty string.
        _ => Fallback,
    };
}
