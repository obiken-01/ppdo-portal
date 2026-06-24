namespace PPDO.Domain.Entities;

/// <summary>
/// Configurable organisational division, scoped to an <see cref="Office"/> (v1.2 — RAL-97).
///
/// Replaces the former fixed <c>Division</c> enum AND the <c>PermissionGroup</c> table:
/// a division is BOTH a data-scoping dimension (which budget-planning / inventory data a
/// user may see) AND the carrier of feature-permission flags ("the grouping"). A Staff
/// user's effective flags resolve from their division's <c>Can*</c> flags plus per-user
/// overrides on <see cref="User"/>.
///
/// Office-scoped: PPDO's divisions differ from other offices'. Seeded via the division
/// config page CSV upload — there is no EF seed migration.
/// </summary>
public sealed class Division
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the owning office (<c>offices.id</c>).</summary>
    public int OfficeId { get; set; }

    /// <summary>
    /// Optional short code, e.g. "ADMIN", "ICT". Nullable — some divisions have no official
    /// code, in which case <see cref="Name"/> is the identifier. Max 20 characters.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>Full division name — the upsert key within an office. Max 200 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Soft-delete flag. Inactive divisions are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    // ── Feature permission flags (the "grouping") ─────────────────────────────
    // Defaults for Staff members of this division. Per-user Override* flags on User
    // take precedence when non-null. SuperAdmin/Admin ignore these (they bypass).
    // CanManageAllocation is deliberately NOT here — it is a per-user grant.

    public bool CanAccessInventory { get; set; }
    public bool CanAccessReports { get; set; }
    public bool CanManageUsers { get; set; }
    public bool CanManageResourceLinks { get; set; }
    public bool CanAccessBudgetPlanning { get; set; }
    public bool CanUploadAip { get; set; }
    public bool CanManageConfig { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The office this division belongs to.</summary>
    public Office? Office { get; set; }

    /// <summary>Users assigned to this division.</summary>
    public ICollection<User> Users { get; set; } = new List<User>();
}
