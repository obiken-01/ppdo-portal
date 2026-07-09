namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: a named, reusable procurement line-item template scoped to an
/// <see cref="Account"/> (v1.4 WFP Rework — RAL-119). Captured from the WFP entry flow's
/// "Save as preset" action OR curated directly on the config page — both write here.
/// Shared across all offices/divisions (§7.2 ✔ resolved 2026-07-07); <see cref="CreatedById"/>
/// is kept for traceability only, never used to scope visibility.
/// Loading a preset into an entry always copies its items (snapshot, editable) — presets are
/// templates, not live links to <see cref="Account"/> or the price index.
/// Soft delete via IsActive only — never hard-delete a referenced preset.
/// </summary>
public sealed class ProcurementPreset
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the Chart of Accounts config record this preset is scoped to.</summary>
    public int AccountId { get; set; }

    /// <summary>Preset name/label. Max 200 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Soft-delete flag. Inactive presets are hidden from "Load preset" but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>FK to the User who created this preset — traceability only, not a visibility scope.</summary>
    public Guid CreatedById { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The account this preset is scoped to.</summary>
    public Account Account { get; set; } = null!;

    /// <summary>The user who created this preset.</summary>
    public User? CreatedBy { get; set; }

    /// <summary>The template's line items.</summary>
    public ICollection<ProcurementPresetItem> Items { get; set; } = new List<ProcurementPresetItem>();
}
