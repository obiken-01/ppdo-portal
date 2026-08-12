namespace PPDO.Domain.Entities;

/// <summary>
/// Tracks which Division received how many units from a specific DeliveryItem.
/// Enables split-delivery: a single delivered batch can be distributed across
/// multiple Divisions with individual IssueRef values per distribution line.
///
/// IssueRef format: ISS-YYYYMMDD-XXXXX-N  (5-digit random + sequence suffix, Manila timezone UTC+8)
/// </summary>
public sealed class Distribution
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Unique issue reference in the format ISS-YYYYMMDD-XXXXX-N.
    /// Generated using Manila time (UTC+8). N is a sequence number within the delivery.
    /// </summary>
    public string IssueRef { get; set; } = string.Empty;

    /// <summary>
    /// FK to the DeliveryItem this distribution is for. Null when sourced from a warehouse
    /// stock count instead of a delivery (RAL-223) — exactly one of
    /// <see cref="DeliveryItemId"/> / <see cref="StockBalanceId"/> is set.
    /// </summary>
    public Guid? DeliveryItemId { get; set; }

    /// <summary>
    /// FK to the StockBalance (warehouse physical-count) entry this distribution was issued
    /// against, when the source stock came from a count rather than a delivery (RAL-223).
    /// Points to whichever entry was "latest" for the StockNo at allocation time — the
    /// warehouse-count pool is a running total, not per-entry batches, so this is provenance
    /// only, not an exact-remaining-quantity link.
    /// </summary>
    public Guid? StockBalanceId { get; set; }

    /// <summary>FK to the division that received this allocation of items (v1.2 — RAL-97).</summary>
    public int DivisionId { get; set; }

    /// <summary>Quantity issued to this Division.</summary>
    public decimal QtyIssued { get; set; }

    /// <summary>Date the items were issued to this Division (Manila date).</summary>
    public DateOnly DateIssued { get; set; }

    /// <summary>Name of the staff member who issued the items to this Division.</summary>
    public string IssuedBy { get; set; } = string.Empty;

    /// <summary>Optional remarks / notes for this distribution record.</summary>
    public string? Remarks { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The DeliveryItem this distribution is part of. Null for a warehouse-count-sourced distribution.</summary>
    public DeliveryItem? DeliveryItem { get; set; }

    /// <summary>The StockBalance entry this distribution references. Null for a delivery-sourced distribution.</summary>
    public StockBalance? StockBalance { get; set; }

    /// <summary>The division that received this allocation.</summary>
    public Division? Division { get; set; }
}
