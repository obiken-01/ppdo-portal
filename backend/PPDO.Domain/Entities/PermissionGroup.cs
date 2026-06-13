using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// A named set of feature-access flags assigned to a Division.
/// Users in the group inherit these flags; individual overrides on User can
/// grant or revoke access per-user without changing the group.
///
/// Default groups are seeded on first migration — group names are referenced
/// in user creation logic and must not be renamed after seeding.
/// </summary>
public sealed class PermissionGroup
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Display name — unique. e.g. "Admin Division Staff", "Observer Default".
    /// Referenced by name in user creation logic; do not rename after seeding.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The Division this group is the default for.
    /// Null for the "Observer Default" group, which spans no specific division.
    /// </summary>
    public Division? Division { get; set; }

    /// <summary>Optional description / notes for admin reference.</summary>
    public string? Description { get; set; }

    // ── Feature permission flags ──────────────────────────────────────────────
    // These are the defaults for group members. Individual User overrides take
    // precedence when set (non-null). SuperAdmin and Admin always have full access
    // regardless of these flags.

    /// <summary>
    /// Grants access to the full Inventory module:
    /// Create PR, Receive Delivery, Items Master, Item Ledger, PR Register, Excel import.
    /// </summary>
    public bool CanAccessInventory { get; set; }

    /// <summary>Grants access to the PR Report — view and Excel export.</summary>
    public bool CanAccessReports { get; set; }

    /// <summary>Grants access to User Management — add, reset password, deactivate.</summary>
    public bool CanManageUsers { get; set; }

    /// <summary>
    /// Grants access to Resource Links management.
    /// Staff with this flag set to true may add links only (cannot edit or delete).
    /// Admin and SuperAdmin always have full manage access regardless of this flag.
    /// Observer never has this access regardless of this flag.
    /// Added in RAL-34.
    /// </summary>
    public bool CanManageResourceLinks { get; set; }

    // ── Budget Planning flags (v1.1 — RAL-81) ─────────────────────────────────

    /// <summary>
    /// Grants access to the Budget Planning module (dashboard, LDIP, AIP view, WFP).
    /// Observer members get read-only access by role. The "Office User Default" group
    /// sets this true; it is the only feature available to non-PPDO office users.
    /// </summary>
    public bool CanAccessBudgetPlanning { get; set; }

    /// <summary>
    /// Grants AIP upload / import preview / confirm. Resolved as PPDO-users-only:
    /// the uploaded file contains every office's records, so non-PPDO office users
    /// can never effectively hold this, and Observer never holds it.
    /// </summary>
    public bool CanUploadAip { get; set; }

    /// <summary>
    /// Grants access to all configuration pages (Accounts, Offices, Funding Sources).
    /// One flag for the whole config section — not per page. Observer never holds it.
    /// </summary>
    public bool CanManageConfig { get; set; }

    // CanAccessProfile is always true for all users — not stored here.

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>Users who belong to this permission group.</summary>
    public ICollection<User> Users { get; set; } = new List<User>();
}
