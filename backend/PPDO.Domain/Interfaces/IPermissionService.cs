using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Resolves effective feature permissions for an authenticated user.
/// Implemented in PPDO.Application/Services/PermissionService.cs.
///
/// Resolution chain (from CLAUDE.md RBAC rules):
///   SuperAdmin / Admin     → true (always — group/override flags ignored)
///   Staff / Observer       → override ?? group flag
///
/// Exceptions:
///   CanManageUsers — Observer always returns false regardless of override.
///   CanManageResourceLinks — Observer always returns false regardless of override.
///   CanAccessProfile — always true for all roles.
///
/// Always call these methods for permission checks in Function handlers.
/// Never inline the resolution logic.
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

    /// <summary>
    /// True when the user may access User Management (add, reset password, deactivate).
    /// Observer always returns false, regardless of any individual override.
    /// </summary>
    Task<bool> CanManageUsersAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// Always true — all authenticated roles can view and edit their own profile.
    /// </summary>
    Task<bool> CanAccessProfileAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user may manage Resource Links.
    /// SuperAdmin/Admin: always true (full manage — add, edit, delete).
    /// Staff: group + override flag (add only — edit/delete require Admin regardless).
    /// Observer: always false, regardless of any override.
    /// </summary>
    Task<bool> CanManageResourceLinksAsync(User user, CancellationToken cancellationToken = default);

    // ── Budget Planning (v1.1 — RAL-81) ───────────────────────────────────────

    /// <summary>
    /// True when the user may access the Budget Planning module (dashboard, LDIP, AIP, WFP).
    /// SuperAdmin/Admin: always true. Staff/Observer: override ?? group flag.
    /// Observer is allowed (read-only by role) — NOT hard-blocked.
    /// </summary>
    Task<bool> CanAccessBudgetPlanningAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user may upload / import AIP files.
    /// SuperAdmin/Admin: always true. PPDO Staff: override ?? group flag.
    /// Observer: always false. Non-PPDO office users (OfficeId set): always false —
    /// the uploaded file contains every office's records, so upload is PPDO-only.
    /// </summary>
    Task<bool> CanUploadAipAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when the user may manage configuration (Accounts, Offices, Funding Sources).
    /// SuperAdmin/Admin: always true. Staff: override ?? group flag.
    /// Observer: always false, regardless of any override.
    /// </summary>
    Task<bool> CanManageConfigAsync(User user, CancellationToken cancellationToken = default);
}
