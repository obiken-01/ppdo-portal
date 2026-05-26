namespace PPDO.Domain.Enums;

/// <summary>
/// Lifecycle status of a Purchase Request.
/// Transitions: Open → PartiallyDelivered → FullyDelivered → Completed
/// Status is updated automatically when a delivery is submitted via DeliveryService.
/// </summary>
public enum PRStatus
{
    /// <summary>No delivery has been recorded against this PR yet.</summary>
    Open = 0,

    /// <summary>At least one delivery has been recorded, but not all items are fully delivered.</summary>
    PartiallyDelivered = 1,

    /// <summary>All line items have been fully delivered (QtyDelivered >= Quantity for every PRItem).</summary>
    FullyDelivered = 2,

    /// <summary>PR has been closed/archived by an Admin after full delivery and distribution.</summary>
    Completed = 3,
}
