namespace PPDO.Domain.Entities;

/// <summary>
/// Tracks how much of a division's fiscal-year allocation each WFP record has used
/// (v1.4 WFP Rework — §8, RAL-122). One row per (DivisionId, FiscalYear, WfpRecordId),
/// upserted whenever that WFP record's expenditure totals change.
///
/// "Remaining" for a division+FY is <c>DivisionAllocation.Amount</c> minus the sum of
/// <see cref="UsedAmount"/> across all this division+FY's ledger rows — a query against
/// this table, never a live SUM across <c>wfp_expenditures</c> directly (§11 Q7).
///
/// WFP-scoped by design (not a generic polymorphic ledger) — see
/// docs/v1.4/WFP_Rework_Requirements_Draft.md §8 for the rationale. Named/shaped so a
/// future consumer of the same allocation could post its own rows later without needing
/// a redesign, but that generalization is explicitly out of scope for this ticket.
/// </summary>
public sealed class WfpDivisionAllocationLedger
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the division this row's allocation belongs to.</summary>
    public int DivisionId { get; set; }

    /// <summary>Fiscal year — matches the WFP record's fiscal year.</summary>
    public int FiscalYear { get; set; }

    /// <summary>FK to the WFP record this usage is attributed to.</summary>
    public int WfpRecordId { get; set; }

    /// <summary>
    /// Snapshot of <c>DivisionAllocation.Amount</c> at the time this row was last upserted —
    /// for audit/history only; the live cap check always re-reads the current allocation.
    /// </summary>
    public decimal AllocatedAmountSnapshot { get; set; }

    /// <summary>Sum of this WFP record's expenditure totals (TotalAppropriation) as of the last upsert.</summary>
    public decimal UsedAmount { get; set; }

    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The division this row's allocation belongs to.</summary>
    public Division Division { get; set; } = null!;

    /// <summary>The WFP record this usage is attributed to.</summary>
    public WfpRecord WfpRecord { get; set; } = null!;
}
