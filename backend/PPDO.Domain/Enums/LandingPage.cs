namespace PPDO.Domain.Enums;

/// <summary>
/// The page a user lands on after signing in (RAL-251).
///
/// Stored as a stable key on <c>Users</c>, <c>divisions</c> and <c>offices</c> — never as a raw
/// path. A stored "/inventory" would silently rot the next time a route is renamed, and the
/// route it points at is a frontend concern the database has no business knowing.
///
/// Values are pinned explicitly: these are persisted as integers (matching <see cref="UserRole"/>),
/// so inserting a member in the middle without a value would silently repoint every existing row.
/// Add new members at the end with the next free number.
/// </summary>
public enum LandingPage
{
    /// <summary>Main portal dashboard. Not offered to office users — the portal layout gate
    /// bounces them off it, so landing them there would loop.</summary>
    MainDashboard = 1,

    /// <summary>Inventory dashboard. Requires <c>CanAccessInventory</c>.</summary>
    InventoryDashboard = 2,

    /// <summary>Budget Planning dashboard. Requires <c>CanAccessBudgetPlanning</c>.</summary>
    BudgetPlanningDashboard = 3,

    /// <summary>The user's own account page. Always reachable — <c>CanAccessProfile</c> is
    /// unconditionally true, which is why this is the terminal fallback.</summary>
    Profile = 4,
}
