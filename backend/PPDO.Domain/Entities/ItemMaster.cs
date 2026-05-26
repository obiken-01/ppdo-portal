namespace PPDO.Domain.Entities;

/// <summary>
/// The office supply catalog — the source of truth for stock numbers,
/// descriptions, units, and unit costs used across all Purchase Requests.
///
/// When a PR is submitted with an item whose StockNo is not found here,
/// the item is added with IsNewItem = true pending admin review.
/// The Create PR form supports bidirectional lookup: entering a StockNo
/// auto-fills Description (and vice versa) from this catalog.
/// </summary>
public sealed class ItemMaster
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Stock number — unique catalog identifier. e.g. "01-01-01-01".</summary>
    public string StockNo { get; set; } = string.Empty;

    /// <summary>Full item description. Required.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Admin-assigned category. Null or empty signals a new/unreviewed item
    /// (displayed as "★ NEW - review" in the Items Master UI).
    /// </summary>
    public string? Category { get; set; }

    /// <summary>Unit of measure. e.g. "ream", "box", "piece", "bottle".</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Current unit cost in Philippine Peso (₱).</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Item type / classification. Optional. e.g. "Office Supplies", "Cleaning Materials".</summary>
    public string? ItemType { get; set; }

    /// <summary>
    /// Quantity threshold for low-stock alerts on the Inventory Dashboard.
    /// When remaining stock falls at or below this value, a low-stock alert is shown.
    /// </summary>
    public int ReorderQty { get; set; }

    /// <summary>Optional admin remarks / notes on this item.</summary>
    public string? Remarks { get; set; }

    /// <summary>
    /// True when this item was added via Create PR and has not yet been reviewed by an Admin.
    /// Displayed with the "★ NEW - review" flag in the Items Master UI.
    /// Set to false by Admin after category assignment and review.
    /// </summary>
    public bool IsNewItem { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
