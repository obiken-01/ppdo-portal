namespace PPDO.Application.DTOs.Delivery;

/// <summary>
/// A single item line in a delivery submission.
/// PRItemId links to the specific PR line item being delivered.
/// Distributions lists how QtyDelivered is split across divisions.
/// </summary>
public sealed record CreateDeliveryItemDto
{
    public required Guid PRItemId { get; init; }
    public required decimal QtyDelivered { get; init; }
    public required IReadOnlyList<CreateDistributionDto> Distributions { get; init; }
}
