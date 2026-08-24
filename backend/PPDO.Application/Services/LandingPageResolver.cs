using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Resolves a user's landing page (RAL-251).
///
/// Chain: <c>user preference → division default → office default → first reachable → Profile</c>.
///
/// Two rules make the result safe to redirect to without further checking:
///
///   1. **Every candidate is permission-checked, including stored preferences.** A saved
///      preference the user can no longer reach (their inventory access was revoked, say) is
///      skipped rather than returned. Returning it would produce a redirect loop, not an error —
///      the target page ejects them, and whatever sent them there sends them straight back.
///   2. **<see cref="LandingPage.Profile"/> is the terminal fallback.** It maps to /account, the
///      one page every authenticated user can always open (<c>CanAccessProfile</c> is
///      unconditionally true), which is also why the existing office-user gate falls back to it.
///
/// The "role default" step in the original plan is folded into the ordered fallback list — a
/// separate per-role default could contradict the list and reintroduce the loop in rule 1.
///
/// No database access. <paramref name="user"/> must be loaded with <see cref="User.Division"/>
/// and <see cref="User.Office"/> — <c>JwtMiddleware.ValidateAsync</c> includes both. Note that
/// <c>UserRepository.FindByUsernameAsync</c> (the login path) does NOT include Office, so a user
/// loaded that way would silently skip the office default.
/// </summary>
public sealed class LandingPageResolver : ILandingPageResolver
{
    private readonly IPermissionService _permissions;

    public LandingPageResolver(IPermissionService permissions) => _permissions = permissions;

    /// <inheritdoc />
    public async Task<LandingPage> ResolveAsync(
        User user,
        CancellationToken cancellationToken = default)
    {
        LandingPage?[] preferences = [
            user.LandingPage,
            user.Division?.LandingPage,
            user.Office?.LandingPage,
        ];

        foreach (LandingPage? preference in preferences)
        {
            if (preference is LandingPage page
                && await IsReachableAsync(user, page, cancellationToken))
                return page;
        }

        foreach (LandingPage candidate in FallbackOrder(user))
        {
            if (await IsReachableAsync(user, candidate, cancellationToken))
                return candidate;
        }

        return LandingPage.Profile;
    }

    /// <inheritdoc />
    public async Task<bool> IsReachableAsync(
        User user,
        LandingPage page,
        CancellationToken cancellationToken = default)
        => page switch
        {
            // Office users are bounced off the main dashboard by the portal layout gate, so
            // offering it to them would loop. PPDO-internal users always have it.
            LandingPage.MainDashboard => user.OfficeId is null,

            LandingPage.InventoryDashboard =>
                await _permissions.CanAccessInventoryAsync(user, cancellationToken),

            LandingPage.BudgetPlanningDashboard =>
                await _permissions.CanAccessBudgetPlanningAsync(user, cancellationToken),

            // /account — always reachable, which is what makes it a safe terminal fallback.
            LandingPage.Profile => true,

            _ => false,
        };

    /// <summary>
    /// Order tried when nothing usable is configured. Office users only ever have Budget
    /// Planning, so listing the others for them would just burn permission checks.
    /// </summary>
    private static LandingPage[] FallbackOrder(User user) =>
        user.OfficeId is null
            ? [LandingPage.MainDashboard,
               LandingPage.InventoryDashboard,
               LandingPage.BudgetPlanningDashboard,
               LandingPage.Profile]
            : [LandingPage.BudgetPlanningDashboard,
               LandingPage.Profile];
}
