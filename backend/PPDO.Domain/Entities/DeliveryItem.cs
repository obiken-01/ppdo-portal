namespace PPDO.Domain.Entities;

/// <summary>
/// A single item line within a Delivery event.
/// Links a Delivery to a specific PRItem and records the quantity delivered.
///
/// QtyDelivered is the total for this delivery event.
/// The item can then be split across multiple Divisions via Distribution records.
/// Split delivery rule: sum of Distribution.QtyIssued must equal QtyDelivered.
/// </summary>
public sealed class DeliveryItem
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>FK to the parent Delivery event.</summary>
    public Guid DeliveryId { get; set; }

    /// <summary>FK to the PRItem that was delivered.</summary>
    public Guid PRItemId { get; set; }

    /// <summary>
    /// Total quantity delivered in this delivery event for this item.
    /// The item may be split across Divisions — see Distribution.QtyIssued.
    /// </summary>
    public decimal QtyDelivered { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The Delivery event this item belongs to.</summary>
    public Delivery? Delivery { get; set; }

    /// <summary>The PR line item that was delivered.</summary>
    public PRItem? PRItem { get; set; }

    /// <summary>
    /// Per-division distribution records for this delivery item.
    /// One item can be split across multiple divisions (e.g. 10 reams to Admin, 5 to Planning).
    /// </summary>
    public ICollection<Distribution> Distributions { get; set; } = new List<Distribution>();
}
