namespace PPDO.Application.DTOs.Delivery;

/// <summary>
/// A single item line in a delivery submission.
/// PRItemId links to the specific PR line item being delivered.
/// Distributions is now optional — distribution is handled separately
/// via POST /api/distributions after delivery is recorded.
/// </summary>
public sealed record CreateDeliveryItemDto
{
    public required Guid PRItemId { get; init; }
    public required decimal QtyDelivered { get; init; }
    public IReadOnlyList<CreateDistributionDto> Distributions { get; init; }
        = Array.Empty<CreateDistributionDto>();
}
