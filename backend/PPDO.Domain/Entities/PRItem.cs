namespace PPDO.Domain.Entities;

/// <summary>
/// A single line item on a Purchase Request (Section 2 of the PR form).
/// ItemNo is sequential within the PR (1, 2, 3, …).
/// TotalCost = Quantity × UnitCost — computed by PurchaseRequestService.
///
/// StockNo and Description support bidirectional lookup from ItemMaster:
/// entering either field in the UI auto-fills the other.
/// </summary>
public sealed class PRItem
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the owning PurchaseRequest.</summary>
    public Guid PRId { get; set; }

    /// <summary>Line item sequence number within the PR (1-based).</summary>
    public int ItemNo { get; set; }

    /// <summary>
    /// Stock number from ItemMaster. Optional — may be null if the item is not yet
    /// in the catalog (flagged as IsNewItem = true in ItemMaster).
    /// </summary>
    public string? StockNo { get; set; }

    /// <summary>Item description. Required. Auto-filled from ItemMaster when StockNo is entered.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Unit of measure. e.g. "ream", "box", "piece". Taken from ItemMaster.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Quantity requested.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Unit cost in Philippine Peso (₱). Taken from ItemMaster.</summary>
    public decimal UnitCost { get; set; }

    /// <summary>Total cost = Quantity × UnitCost. Computed by PurchaseRequestService.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Item type / classification. Optional. Taken from ItemMaster.</summary>
    public string? ItemType { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The Purchase Request this line item belongs to.</summary>
    public PurchaseRequest? PurchaseRequest { get; set; }
}
