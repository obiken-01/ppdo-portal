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
    /// FK to the configurable <see cref="Entities.Division"/> this user belongs to (v1.2 — RAL-97).
    /// Replaces the former Division enum AND GroupId: the division carries both the user's
    /// data scope AND their default feature flags.
    ///
    /// Null for SuperAdmin/Admin (they bypass/default all flags).
    /// ⚠️ A null DivisionId on a Staff user must resolve to an EMPTY inventory scope,
    /// never "all divisions" — see DivisionScope and the Inventory/Distribution guards.
    /// </summary>
    public int? DivisionId { get; set; }

    /// <summary>
    /// FK to the provincial office this user belongs to (<c>offices.id</c>). New in v1.1.
    /// The PPDO / non-PPDO discriminator:
    ///   null  → PPDO-internal user
    ///   set   → non-PPDO office user, scoped to that office's budget planning data only.
    /// </summary>
    public int? OfficeId { get; set; }

    /// <summary>Job title / position. Optional, max 100 characters.</summary>
    public string? Position { get; set; }

    /// <summary>Contact number. Optional.</summary>
    public string? ContactNo { get; set; }

    /// <summary>Soft-delete flag. Deactivated users cannot log in.</summary>
    public bool IsActive { get; set; } = true;

    // ── Individual permission overrides ───────────────────────────────────────
    // null  = inherit from the user's Division flags
    // true  = explicitly granted (overrides the division flag)
    // false = explicitly revoked (overrides the division flag)
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
    /// Override for Configuration management (Accounts, Offices, Funding Sources, Divisions).
    /// Null = use Division.CanManageConfig. Ignored for SuperAdmin and Admin.
    /// </summary>
    public bool? OverrideCanManageConfig { get; set; }

    /// <summary>
    /// Per-user grant for the Budget Allocation page (v1.2 — RAL-97). Unlike the other
    /// flags this is NOT a division flag: it is assigned to a specific finance-officer user
    /// regardless of role/division. Resolution: SuperAdmin → true; everyone else →
    /// <c>OverrideCanManageAllocation ?? false</c> (Admin is NOT auto-granted this).
    /// </summary>
    public bool? OverrideCanManageAllocation { get; set; }

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

    /// <summary>The division this user belongs to. Null for SuperAdmin/Admin. Carries feature flags.</summary>
    public Division? Division { get; set; }

    /// <summary>The provincial office this user belongs to. Null for PPDO-internal users.</summary>
    public Office? Office { get; set; }
}
