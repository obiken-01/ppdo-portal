namespace PPDO.Domain.Entities;

/// <summary>
/// One physical-count entry in the warehouse stock ledger (RAL-193).
///
/// PPDO-wide (no division scope) — the on-hand formula folds these into the
/// Admin/SuperAdmin unscoped view only (see <c>InventoryService</c>); Staff/Observer's
/// division-scoped on-hand is unaffected.
///
/// <see cref="VarianceQty"/> is computed once at save time as
/// <see cref="CountedQty"/> - <see cref="SystemOnHandAtEntry"/> and persisted directly —
/// the running SUM of <see cref="VarianceQty"/> across all entries for a StockNo is the
/// "opening balance" term added to <c>QtyDelivered - QtyDistributed</c>.
/// </summary>
public sealed class StockBalance
{
    public Guid Id { get; set; }

    /// <summary>Catalog stock number. Not FK-constrained to ItemMaster — matches the
    /// loosely-coupled StockNo-keyed pattern used elsewhere in Inventory.</summary>
    public string StockNo { get; set; } = string.Empty;

    /// <summary>The quantity physically counted in the warehouse.</summary>
    public decimal CountedQty { get; set; }

    /// <summary>Snapshot of what the system computed as on-hand for this StockNo at the
    /// moment this entry was saved (before this entry's own variance is applied).</summary>
    public decimal SystemOnHandAtEntry { get; set; }

    /// <summary>CountedQty - SystemOnHandAtEntry. Persisted so the running on-hand formula
    /// is a plain SUM over this column — no need to re-derive it on every read.</summary>
    public decimal VarianceQty { get; set; }

    /// <summary>The date the physical count was actually taken (may be backdated from
    /// CreatedAt). Descriptive/audit only — does not gate the on-hand formula (forward-only
    /// per RAL-193: a new entry only affects computations from now on, never retroactively).</summary>
    public DateOnly EffectiveDate { get; set; }

    /// <summary>Optional free-text reason/note, e.g. "Quarterly physical count", "Found
    /// during audit".</summary>
    public string? Reason { get; set; }

    /// <summary>The user who recorded this entry.</summary>
    public Guid RecordedByUserId { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
