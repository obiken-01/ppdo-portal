namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// PR Report response — the three sections of the official PR Report form.
///
/// Section 1 + 2 are carried by <see cref="PR"/> (header fields + line items).
/// Section 3 is the flat distribution list.
///
/// <see cref="DeliveryItems"/> is a separate flat list of every DeliveryItem
/// for this PR — it does NOT depend on distributions being present, so the
/// report correctly shows qty-delivered even when distribution has not been
/// recorded yet (post-separation of Receive Delivery and Distribution).
/// </summary>
public sealed record PRReportDto(
    /// <summary>Section 1 (header fields) and Section 2 (line items).</summary>
    PRResponseDto PR,
    /// <summary>Section 3 — flat distribution rows, ordered by ItemNo then DeliveryDate.</summary>
    IReadOnlyList<PRReportDistributionDto> Distributions,
    /// <summary>
    /// Flat list of all DeliveryItem rows for this PR — one entry per item per delivery.
    /// Used by the frontend to compute qty-delivered and delivery count independently
    /// of whether distributions have been recorded.
    /// </summary>
    IReadOnlyList<PRReportDeliveryItemDto> DeliveryItems);

/// <summary>
/// One delivery item row — ItemNo + delivery context + qty received.
/// Does not require distributions to exist.
/// </summary>
public sealed record PRReportDeliveryItemDto(
    int      ItemNo,
    string   DeliveryRef,
    DateOnly DeliveryDate,
    decimal  QtyDelivered);
