namespace PPDO.Application.DTOs.Delivery;

/// <summary>
/// Request body for POST /api/deliveries.
/// PRId links to the Purchase Request being delivered against.
/// </summary>
public sealed record CreateDeliveryDto
{
    public required Guid PRId { get; init; }
    public required DateOnly DeliveryDate { get; init; }
    public required string ReceivedBy { get; init; }
    public string? Supplier { get; init; }
    public string? Remarks { get; init; }
    public required IReadOnlyList<CreateDeliveryItemDto> Items { get; init; }
}
