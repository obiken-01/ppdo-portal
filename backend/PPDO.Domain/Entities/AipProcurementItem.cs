namespace PPDO.Domain.Entities;

/// <summary>
/// One procurement line item under an <see cref="AipExpenditure"/>, sourced from the Price Index
/// (v1.8.0 Phase 3 — V18-80 / PPDO-54).
///
/// <b>Why this is not <see cref="WfpProcurementItem"/>.</b> A WFP expenditure is a full
/// <i>schedule</i> — monthly or quarterly frequency, an annual-quarter choice, a reserve. An AIP
/// activity still carries <b>one annual figure</b>, and none of those come across.
///
/// ↩️ <b><see cref="PeriodNo"/> did come across, on 2026-09-14</b> (Ralph, after a full-cycle test).
/// V18-80 originally refused every period field. Items are now placed in a quarter, Q1–Q4, the way
/// WFP's quarterly items are — but <b>as input only</b>. The line's amount is still Σ of all its items
/// across the four quarters, so the printed form, the ceiling and the consolidated grid see the same
/// single annual figure they always did. Do not let a quarter reach any of them.
///
/// <b><see cref="NumberOfDays"/> is the one exception to that rule, and it is deliberate.</b> It
/// looks exactly like the scheduling fields being stripped, and it stays: PPDO employees asked for
/// it, so it is a requirement in its own right rather than a copied artefact (settled 2026-09-07).
/// Do not remove it while removing the period dimension.
///
/// <b>The snapshot columns are not redundant</b>, the same reason
/// <see cref="AipExpenditure"/>'s are not. <see cref="Name"/>, <see cref="Unit"/> and
/// <see cref="UnitPrice"/> record what the Price Index said <i>at save time</i>; the FK records
/// which catalogue row it was. A later price-index edit must never retroactively change a saved
/// AIP, and an auditor asks the first question, not the second.
///
/// <b>An AIP procurement line is a plan, not a commitment.</b> It is not linked to any WFP
/// procurement item and this table carries no FK to one — the netting mechanism between the two
/// documents is deferred (Phase_Plan §12.1, with V18-45).
/// </summary>
public sealed class AipProcurementItem
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent expenditure. Cascade delete — an item cannot outlive its line.</summary>
    public int ExpenditureId { get; set; }

    /// <summary>
    /// The quarter this item is planned in, 1–4 (added 2026-09-14). ⚠️ Input only — the line's amount
    /// sums every quarter, and nothing downstream of the line reads this. Items saved before the
    /// column existed were placed in Q1, which leaves every saved total unchanged.
    /// </summary>
    public int PeriodNo { get; set; } = 1;

    /// <summary>
    /// FK to the Price Index config row this item was picked from. Null for a free-typed item.
    ///
    /// ⚠️ Also what the duplicate warning keys on, so a free-typed item is never reported as a
    /// duplicate of anything — two rows both typed by hand are not known to be the same item.
    /// </summary>
    public int? PriceIndexItemId { get; set; }

    /// <summary>Snapshot of the item name at save time. Max 300 characters.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Snapshot of the unit at save time. Max 50 characters.</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>Snapshot of the unit price at save time, in pesos.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Quantity.</summary>
    public decimal Qty { get; set; }

    /// <summary>
    /// Number of days this item runs for. Multiplies into <see cref="LineTotal"/> for day-based
    /// procurement (venue rental, per-diem, honoraria). Defaults to 1 for everything else.
    ///
    /// ⚠️ Kept deliberately — see this type's remarks. It is not a leftover of the WFP period model.
    /// </summary>
    public decimal NumberOfDays { get; set; } = 1m;

    /// <summary>
    /// <see cref="Qty"/> × <see cref="UnitPrice"/> × <see cref="NumberOfDays"/>, in pesos.
    ///
    /// ⚠️ <b>Computed on write via <see cref="Recalculate"/>, never accepted from a caller</b> —
    /// the same rule as <see cref="AipExpenditure.Total"/>, and for the same reason (RAL-144).
    /// </summary>
    public decimal LineTotal { get; private set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent expenditure line.</summary>
    public AipExpenditure Expenditure { get; set; } = null!;

    /// <summary>The Price Index config row this item was picked from. Null for a free-typed item.</summary>
    public PriceIndexItem? PriceIndexItem { get; set; }

    /// <summary>
    /// Recomputes <see cref="LineTotal"/> from its three factors. Call after any change to
    /// <see cref="Qty"/>, <see cref="UnitPrice"/> or <see cref="NumberOfDays"/>.
    /// </summary>
    public void Recalculate() => LineTotal = Qty * UnitPrice * NumberOfDays;
}
