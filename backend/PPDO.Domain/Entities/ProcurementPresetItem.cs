namespace PPDO.Domain.Entities;

/// <summary>
/// One line item on a <see cref="ProcurementPreset"/> template (v1.4 WFP Rework — RAL-119).
/// When <see cref="PriceIndexItemId"/> is set, Name/Unit/UnitPrice are a SNAPSHOT taken at
/// save time — a later price-index update must never retroactively change a saved preset
/// (same rule as <c>WfpProcurementItem</c>). When null, Name/Unit/UnitPrice were free-typed
/// by the user and have no link back to the price index at all.
/// </summary>
public sealed class ProcurementPresetItem
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent preset.</summary>
    public int PresetId { get; set; }

    /// <summary>
    /// FK to the price index item this line was picked from, if any. Null for a free-typed
    /// line item with no price-index link.
    /// </summary>
    public int? PriceIndexItemId { get; set; }

    /// <summary>Item name — snapshot from the price index item, or free-typed. Max 300 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Unit of measure — snapshot from the price index item, or free-typed. Max 50 characters.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Unit price — snapshot from the price index item, or free-typed.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Default quantity applied when this preset is loaded into an entry.</summary>
    public decimal DefaultQty { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent preset.</summary>
    public ProcurementPreset Preset { get; set; } = null!;

    /// <summary>The price index item this line was picked from, if any.</summary>
    public PriceIndexItem? PriceIndexItem { get; set; }
}
