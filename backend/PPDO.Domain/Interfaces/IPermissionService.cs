using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Resolves effective feature permissions for an authenticated user.
/// Implemented in PPDO.Application/Services/PermissionService.cs.
///
/// Resolution chain (v1.2 — RAL-97):
///   SuperAdmin           → true for everything (full bypass)
///   Admin                → true for every flag EXCEPT special per-user grants (allocation)
///   Staff                → Override ?? user.Division.&lt;flag&gt; ?? false
///
/// Special cases:
///   CanManageAllocation — per-user grant only: SuperAdmin → true, else Override ?? false
///                         (Admin is NOT auto-granted this).
///   CanAccessProfile    — always true for all roles.
///
/// Always call these methods for permission checks in Function handlers.
/// Never inline the resolution logic. The user's <see cref="User.Division"/> navigation
/// must be loaded (JwtMiddleware guarantees this).
/// </summary>
public interface IPermissionService
{
    /// <summary>
    /// True when the user may access the full Inventory module:
    /// Create PR, Receive Delivery, Items Master, Item Ledger, PR Register, Excel import.
    /// </summary>
    Task<bool> CanAccessInventoryAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>True when the user may view and export PR Reports.</summary>
    Task<bool> CanAccessReportsAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>True when the user may access User Management (add, reset password, deactivate).</summary>
    Task<bool> CanManageUsersAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Always true — all authenticated roles can view and edit their own profile.
    /// </summary>
    Task<bool> CanAccessProfileAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user may manage Resource Links.
    /// SuperAdmin/Admin: always true (full manage — add, edit, delete).
    /// Staff: division + override flag (add only — edit/delete require Admin regardless).
    /// </summary>
    Task<bool> CanManageResourceLinksAsync(User user, CancellationToken cancellationToken = default);

    // ── Budget Planning ───────────────────────────────────────────────────────

    /// <summary>
    /// True when the user may access the Budget Planning module (dashboard, LDIP, AIP, WFP).
    /// SuperAdmin/Admin: always true. Staff: override ?? division flag.
    /// </summary>
    Task<bool> CanAccessBudgetPlanningAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user may upload / import AIP files.
    /// SuperAdmin/Admin: always true. PPDO Staff: override ?? division flag.
    /// Non-PPDO office users (OfficeId set): always false — the uploaded file contains every
    /// office's records, so upload is PPDO-only.
    /// </summary>
    Task<bool> CanUploadAipAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user may manage configuration (Accounts, Offices, Funding Sources, Divisions).
    /// SuperAdmin/Admin: always true. Staff: override ?? division flag.
    /// </summary>
    Task<bool> CanManageConfigAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user may access the Budget Allocation page (v1.2 — RAL-97).
    /// Per-user grant only: SuperAdmin → true; everyone else → OverrideCanManageAllocation ?? false.
    /// Admin is NOT auto-granted this — only the designated finance officer holds it.
    /// </summary>
    Task<bool> CanManageAllocationAsync(User user, CancellationToken cancellationToken = default);
}
