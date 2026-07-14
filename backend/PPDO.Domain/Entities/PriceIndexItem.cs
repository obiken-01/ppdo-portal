namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: a procurement item name + unit price catalogue (v1.4 — RAL-118).
/// Data originates from GSO's own application — currently downloaded as an Excel
/// file and uploaded here via CSV (see docs/v1.4/WFP_Rework_Requirements_Draft.md
/// §7.1); this makes the CSV import path the primary real-world ingestion route,
/// not a bonus feature.
/// Searched by name from the WFP procurement line-item entry screen (RAL-125),
/// which snapshots Name/Unit/UnitPrice at the moment an item is picked so a later
/// price change here never retroactively alters a saved WFP line.
/// Upsert key is (Name, Unit) — there is no natural external code for a GSO price
/// item the way FundingSource/Account have Code/AccountNumber.
/// Soft delete via IsActive only — never hard-delete a referenced item.
/// </summary>
public sealed class PriceIndexItem
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>Item name/description. Max 300 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unit of measure (e.g. "ream", "box", "piece", "liter"). Max 50 characters.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Current unit price.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Optional free-text category for grouping/filtering. Max 100 characters.</summary>
    public string? Category { get; set; }

    /// <summary>
    /// UTC timestamp of the last time <see cref="UnitPrice"/> actually changed — surfaced in
    /// search results (RAL-125) so a stale price is visible to the user, not silently trusted.
    /// Set on create and whenever an update changes UnitPrice; untouched by unrelated edits.
    /// </summary>
    public DateTime PriceUpdatedAt { get; set; }

    /// <summary>Soft-delete flag. Inactive items are hidden from search but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Gates the WFP procurement line-item entry screen's "Days" multiplier (v1.4 — RAL-138):
    /// only items where a multi-day duration is meaningful (venue rental, food, accommodation)
    /// enable that field. Default false — most catalogue items don't use it.
    /// </summary>
    public bool DaysEnabled { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
