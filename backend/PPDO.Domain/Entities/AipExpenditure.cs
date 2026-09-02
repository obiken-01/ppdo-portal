namespace PPDO.Domain.Entities;

/// <summary>
/// One expenditure line under an <see cref="AipActivity"/> (v1.8.0 Phase 2 — V18-33).
///
/// <b>Why AIP gets its own table rather than reusing <see cref="WfpExpenditure"/></b> (DECISION A,
/// 2026-08-25). The two documents answer different questions. A WFP expenditure is a *schedule* —
/// quarters, frequency, reserve, procurement items — because WFP asks "when in the year is this
/// spent?". An AIP expenditure is a *composition*: what the activity's PS / MOOE / CO is made of,
/// and nothing about timing. One table serving both would carry six columns that are always null
/// for one of them, and every reader would have to know which kind it was holding.
///
/// <b>Amounts are PESOS</b> (DECISION E, V18-35). This table is created peso-denominated from the
/// start and never had a thousands era — unlike <see cref="AipActivity"/>, which is migrated. Do
/// not add a ×1000 anywhere near it.
///
/// <b>The snapshot columns are not redundant.</b> <see cref="AccountNumberSnapshot"/>,
/// <see cref="AccountTitleSnapshot"/> and <see cref="FundingSourceSnapshot"/> preserve what the
/// config rows said *at the time the line was recorded*, so a historical AIP still prints
/// correctly after somebody renames an account or retires a funding source. The FK answers "which
/// config row is this?"; the snapshot answers "what did it say when this was entered?". Copying
/// this entity's shape without its snapshots quietly loses the second question — which is the one
/// an auditor asks.
/// </summary>
public sealed class AipExpenditure
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the parent AIP activity. Cascade delete — a line cannot outlive its activity.</summary>
    public int ActivityId { get; set; }

    /// <summary>FK to the chart-of-accounts row. Null when unmatched at entry.</summary>
    public int? AccountId { get; set; }

    /// <summary>Snapshot of <see cref="Account.AccountNumber"/> at entry time. Max 20 characters.</summary>
    public string? AccountNumberSnapshot { get; set; }

    /// <summary>Snapshot of <see cref="Account.AccountTitle"/> at entry time. Max 300 characters.</summary>
    public string? AccountTitleSnapshot { get; set; }

    /// <summary>FK to the funding source config row. Null when unmatched at entry.</summary>
    public int? FundingSourceId { get; set; }

    /// <summary>Snapshot of <see cref="FundingSource.Code"/> at entry time. Max 20 characters.</summary>
    public string? FundingSourceSnapshot { get; set; }

    /// <summary>Snapshot of <see cref="FundingSource.Name"/> at entry time. Max 100 characters.</summary>
    public string? FundingSourceNameSnapshot { get; set; }

    /// <summary>Personal Services, in pesos.</summary>
    public decimal Ps { get; set; }

    /// <summary>Maintenance and Other Operating Expenses, in pesos.</summary>
    public decimal Mooe { get; set; }

    /// <summary>Capital Outlay, in pesos.</summary>
    public decimal Co { get; set; }

    /// <summary>
    /// <see cref="Ps"/> + <see cref="Mooe"/> + <see cref="Co"/>, in pesos.
    ///
    /// ⚠️ <b>Always computed on write via <see cref="Recalculate"/> — never accepted from a
    /// caller.</b> RAL-144 is the precedent: <see cref="AipActivity.Total"/> originally trusted the
    /// source file's own Total column, and a blank or stale cell there desynced it from its own
    /// components while every downstream reader carried on believing it. Stored rather than
    /// computed on read because the printable form and the external API both read it in bulk, and
    /// a GROUP BY under a report path is what <c>docs/PERFORMANCE_GUIDELINES.md</c> exists to stop.
    ///
    /// Non-nullable and defaulting to 0: "no money entered" is 0, not null. Null would mean "never
    /// computed", and this property is computed the moment the row exists.
    /// </summary>
    public decimal Total { get; private set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The parent AIP activity.</summary>
    public AipActivity Activity { get; set; } = null!;

    /// <summary>The chart-of-accounts row. Null when unmatched.</summary>
    public Account? Account { get; set; }

    /// <summary>The funding source config row. Null when unmatched.</summary>
    public FundingSource? FundingSource { get; set; }

    /// <summary>
    /// Recomputes <see cref="Total"/> from the three components. Call after any change to
    /// <see cref="Ps"/>, <see cref="Mooe"/> or <see cref="Co"/>; the repository does this on every
    /// save so no caller has to remember.
    /// </summary>
    public void Recalculate() => Total = Ps + Mooe + Co;
}
