namespace PPDO.Domain.Entities;

/// <summary>
/// One procurement line item under a WFP expenditure, for one period (v1.4 WFP Rework —
/// RAL-120). Name/Unit/UnitPrice are snapshotted from <see cref="PriceIndexItem"/> at save
/// time (editable after) — later price-index updates must never silently change a saved WFP.
/// LineTotal is always computed server-side (Qty × UnitPrice), never accepted as client input.
/// Items can differ per period (carry-forward across periods is a UI convenience, not a
/// schema constraint).
/// </summary>
public sealed class WfpProcurementItem
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent expenditure.</summary>
    public int ExpenditureId { get; set; }

    /// <summary>
    /// 1-based period number this item applies to, using the same numbering as
    /// <see cref="WfpExpenditurePeriod.PeriodNo"/> for the expenditure's frequency.
    /// </summary>
    public int PeriodNo { get; set; }

    /// <summary>FK to the Price Index config record this item was picked from. Null for a free-typed item.</summary>
    public int? PriceIndexItemId { get; set; }

    /// <summary>Snapshot of the item name at save time. Max 300 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Snapshot of the unit at save time. Max 50 characters.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Snapshot of the unit price at save time.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Quantity for this period.</summary>
    public decimal Qty { get; set; }

    /// <summary>Computed on save: Qty × UnitPrice. Never accepted as client input.</summary>
    public decimal LineTotal { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent expenditure.</summary>
    public WfpExpenditure Expenditure { get; set; } = null!;

    /// <summary>The Price Index config record this item was picked from. Null for a free-typed item.</summary>
    public PriceIndexItem? PriceIndexItem { get; set; }
}
