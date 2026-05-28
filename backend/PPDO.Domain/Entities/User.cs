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

    /// <summary>Email address — used as login username. Must be unique.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>BCrypt password hash. Never store or log the plain-text password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Determines base permission behaviour. See UserRole XML docs for per-role rules.</summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// The organisational division this user belongs to.
    /// Staff and Observer are restricted to writing data for their own Division.
    /// SuperAdmin and Admin can read/write all Divisions regardless of this value.
    /// </summary>
    public Division Division { get; set; }

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

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The permission group this user belongs to. Null for SuperAdmin/Admin.</summary>
    public PermissionGroup? Group { get; set; }
}
