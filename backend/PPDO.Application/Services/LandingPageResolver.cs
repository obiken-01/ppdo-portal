using PPDO.Application.Common;
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
/// and <see cref="User.Office"/>. Every <c>UserRepository</c> find path includes both as of
/// RAL-258 — Office is no longer optional here, because <c>OfficeScope.IsHostOfficeUser</c> reads
/// the host-office flag off it and a missing Office reads as "guest office".
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
            // Guest-office users are bounced off the main dashboard by the portal layout gate,
            // so offering it to them would loop. Host-office users always have it.
            LandingPage.MainDashboard => OfficeScope.IsHostOfficeUser(user),

            // Inventory is host-office-only and the portal gate blocks it for guest offices, so
            // the office check comes first: a guest division that happens to carry
            // CanAccessInventory would otherwise strand its users on a page that ejects
            // them (RAL-271). Same reasoning as MainDashboard above.
            LandingPage.InventoryDashboard =>
                OfficeScope.IsHostOfficeUser(user)
                && await _permissions.CanAccessInventoryAsync(user, cancellationToken),

            LandingPage.BudgetPlanningDashboard =>
                await _permissions.CanAccessBudgetPlanningAsync(user, cancellationToken),

            // /account — always reachable, which is what makes it a safe terminal fallback.
            LandingPage.Profile => true,

            _ => false,
        };

    /// <summary>
    /// Order tried when nothing usable is configured. Guest-office users only ever have Budget
    /// Planning, so listing the others for them would just burn permission checks.
    /// </summary>
    private static LandingPage[] FallbackOrder(User user) =>
        OfficeScope.IsHostOfficeUser(user)
            ? [LandingPage.MainDashboard,
               LandingPage.InventoryDashboard,
               LandingPage.BudgetPlanningDashboard,
               LandingPage.Profile]
            : [LandingPage.BudgetPlanningDashboard,
               LandingPage.Profile];
}
