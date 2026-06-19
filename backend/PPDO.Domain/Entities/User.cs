using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// A PPDO staff member who can log in to the portal.
/// Password is hashed via BCrypt (managed in Infrastructure/AuthService).
/// Permissions are resolved at runtime: Role → Group flags → Individual overrides.
/// </summary>
public sealed class User
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Full legal name. Max 100 characters.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Login username. Must be unique. Max 50 characters.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Email address — optional, must be unique when set. Max 256 characters.</summary>
    public string? Email { get; set; }

    /// <summary>BCrypt password hash. Never store or log the plain-text password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Determines base permission behaviour. See UserRole XML docs for per-role rules.</summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// The organisational division this user belongs to (PPDO-internal scope).
    /// Staff and Observer are restricted to writing inventory data for their own Division.
    /// SuperAdmin and Admin can read/write all Divisions regardless of this value.
    ///
    /// Nullable from v1.1: non-PPDO office users have no division (see <see cref="OfficeId"/>).
    /// ⚠️ A null Division on a Staff/Observer must resolve to an EMPTY inventory scope,
    /// never "all divisions" — see InventoryService/DistributionService scope guards.
    /// </summary>
    public Division? Division { get; set; }

    /// <summary>
    /// FK to the provincial office this user belongs to (<c>offices.id</c>). New in v1.1.
    /// This is the PPDO / non-PPDO discriminator:
    ///   null  → PPDO-internal user (uses <see cref="Division"/> for scope)
    ///   set   → non-PPDO office user, scoped to that office's budget planning data only.
    /// </summary>
    public int? OfficeId { get; set; }

    /// <summary>
    /// FK to the user's PermissionGroup.
    /// Null for SuperAdmin and Admin — they bypass all flag checks.
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>Job title / position. Optional, max 100 characters.</summary>
    public string? Position { get; set; }

    /// <summary>Contact number. Optional.</summary>
    public string? ContactNo { get; set; }

    /// <summary>Soft-delete flag. Deactivated users cannot log in.</summary>
    public bool IsActive { get; set; } = true;

    // ── Individual permission overrides ───────────────────────────────────────
    // null  = inherit from PermissionGroup
    // true  = explicitly granted (overrides group value)
    // false = explicitly revoked (overrides group value)
    // SuperAdmin and Admin always have full access — these flags are ignored for them.

    /// <summary>
    /// Override for Inventory access. Null = use Group.CanAccessInventory.
    /// Ignored for SuperAdmin and Admin.
    /// </summary>
    public bool? OverrideCanAccessInventory { get; set; }

    /// <summary>
    /// Override for Reports access. Null = use Group.CanAccessReports.
    /// Ignored for SuperAdmin and Admin.
    /// </summary>
    public bool? OverrideCanAccessReports { get; set; }

    /// <summary>
    /// Override for User Management access. Null = use Group.CanManageUsers.
    /// Ignored for SuperAdmin and Admin. Observer can never have this set to true.
    /// </summary>
    public bool? OverrideCanManageUsers { get; set; }

    /// <summary>
    /// Override for Resource Links management access. Null = use Group.CanManageResourceLinks.
    /// Ignored for SuperAdmin and Admin. Observer can never have this set to true.
    /// Added in RAL-34.
    /// </summary>
    public bool? OverrideCanManageResourceLinks { get; set; }

    /// <summary>
    /// Override for Budget Planning access. Null = use Group.CanAccessBudgetPlanning.
    /// Ignored for SuperAdmin and Admin. Added in RAL-81 (v1.1).
    /// </summary>
    public bool? OverrideCanAccessBudgetPlanning { get; set; }

    /// <summary>
    /// Override for AIP upload/import. Null = use Group.CanUploadAip.
    /// Ignored for SuperAdmin and Admin. Observer and non-PPDO office users can never
    /// have this effectively granted (resolved in PermissionService). Added in RAL-81.
    /// </summary>
    public bool? OverrideCanUploadAip { get; set; }

    /// <summary>
    /// Override for Configuration management (Accounts, Offices, Funding Sources).
    /// Null = use Group.CanManageConfig. Ignored for SuperAdmin and Admin.
    /// Observer can never have this effectively granted. Added in RAL-81.
    /// </summary>
    public bool? OverrideCanManageConfig { get; set; }

    // ── Refresh token (JWT rotation) ─────────────────────────────────────────

    /// <summary>
    /// Opaque random token used to obtain a new access token when the current one expires.
    /// Stored as a BCrypt-free base64 string (64 random bytes, 88-char base64).
    /// Null when the user is not logged in or has logged out.
    /// Cleared on logout and rotated on every successful refresh.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// UTC expiry for the refresh token. Null when RefreshToken is null.
    /// Server rejects refresh attempts past this timestamp.
    /// </summary>
    public DateTime? RefreshTokenExpiry { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The permission group this user belongs to. Null for SuperAdmin/Admin.</summary>
    public PermissionGroup? Group { get; set; }

    /// <summary>The provincial office this user belongs to. Null for PPDO-internal users.</summary>
    public Office? Office { get; set; }
}
