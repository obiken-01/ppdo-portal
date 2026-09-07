namespace PPDO.Domain.Entities;

/// <summary>
/// Tracks how much of a division's fiscal-year-and-funding-source allocation each AIP activity has
/// <b>reserved</b> (v1.8.0 Phase 3 — V18-45 / PPDO-55). One row per
/// (DivisionId, FiscalYear, FundingSourceId, AipActivityId), upserted whenever that activity's
/// expenditures for that funding source change.
///
/// <b>⚠️ This is its own table, mirroring <see cref="WfpDivisionAllocationLedger"/> rather than
/// generalising it</b> — DECISION A, 2026-08-25. Ralph, verbatim: <i>"create new tables and fields
/// for AIP, and not reuse the fields used by WFP … in the future, WFP itself will be updated."</i>
/// Same reasoning as <c>aip_expenditures</c> (V18-33): the two documents answer different
/// questions, and one shared table would have to serve both, badly.
/// <c>WfpDivisionAllocationLedger</c> and <c>WfpCeilingService</c> take a <b>zero diff</b> from
/// this ticket.
///
/// <b>Reservation, not commitment.</b> The AIP is a plan: a row here says an activity <i>intends</i>
/// to draw this much. The WFP is the commitment. That distinction is what the deferred netting rule
/// below rests on.
///
/// <h3>⚠️ The netting rule — deferred, NOT absent. Read before implementing it.</h3>
/// The AIP row is a reservation that the WFP <b>relieves</b> as it commits. The two ledgers must
/// <b>net, not add</b>:
/// <code>
/// allocation consumed = WFP committed + AIP reserved not yet converted
/// </code>
/// <b>⚠️ Relief must be per ACTIVITY, not per fund.</b> Per-fund relief strands reservations
/// whenever the fund mix changes: ₱6M reserved as GF, later detailed as ₱4M GF + ₱2M GAD, leaves
/// ₱2M of stale GF reserved forever. <b>This is why rows here are keyed per activity</b> — it is
/// what makes the relief implementable later without a second migration.
///
/// <b>None of that is built yet, and that is correct.</b> V18-81 (PPDO-49) refuses FY2028+ WFP
/// creation in this system, so there is no FY2028 WFP to net against. The deferral is safe
/// <i>only</i> while that refusal stands — remove it and this becomes an open correctness question
/// rather than a closed one.
///
/// <b>⚠️ One gap this leaves, given General-Fund-only ceilings:</b> an activity planned under an
/// unchecked fund and detailed under GF consumes GF allocation with no AIP reservation behind it.
/// Known, accepted, and worth re-reading when netting is built.
///
/// <h3>⚠️ Remaining is never clamped at zero</h3>
/// "Remaining" for a division+FY+fund is <c>DivisionAllocation.Amount</c> minus the sum of
/// <see cref="ReservedAmount"/> across that division+FY+fund's rows — computed, never stored, and
/// <b>never passed through <c>Math.Max(0, …)</c></b> in the query, the DTO or the UI. After PBO
/// cuts a ceiling below encoded work the figure is legitimately <b>negative</b>, and that negative
/// is precisely the state that must block submit (A5-b). Clamping it hides the only signal the
/// office gets.
/// </summary>
public sealed class AipDivisionAllocationLedger
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the division this row's allocation belongs to.</summary>
    public int DivisionId { get; set; }

    /// <summary>Fiscal year — matches the AIP record's fiscal year.</summary>
    public int FiscalYear { get; set; }

    /// <summary>FK to the funding source this row's reservation is attributed to.</summary>
    public int FundingSourceId { get; set; }

    /// <summary>
    /// FK to the AIP activity this reservation belongs to. <b>Per activity, deliberately</b> — see
    /// the netting rule on the class summary; per-fund keying would strand reservations.
    /// </summary>
    public int AipActivityId { get; set; }

    /// <summary>
    /// Snapshot of <c>DivisionAllocation.Amount</c> (for this row's funding source) at the time
    /// this row was last upserted — for audit and history only. The live check always re-reads the
    /// current allocation, so that a ceiling cut takes effect immediately rather than being masked
    /// by a stale snapshot.
    /// </summary>
    public decimal AllocatedAmountSnapshot { get; set; }

    /// <summary>
    /// Sum of this activity's expenditure amounts for this row's funding source, as of the last
    /// upsert. The amount <i>reserved</i>, not spent.
    /// </summary>
    public decimal ReservedAmount { get; set; }

    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The division this row's allocation belongs to.</summary>
    public Division Division { get; set; } = null!;

    /// <summary>The funding source this row's reservation is attributed to.</summary>
    public FundingSource FundingSource { get; set; } = null!;

    /// <summary>The AIP activity this reservation belongs to.</summary>
    public AipActivity AipActivity { get; set; } = null!;
}
