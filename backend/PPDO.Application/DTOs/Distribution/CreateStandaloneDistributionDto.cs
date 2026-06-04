namespace PPDO.Application.DTOs.Distribution;

/// <summary>
/// Request body for POST /api/distributions.
/// Creates a single distribution record against a specific DeliveryItem.
/// QtyIssued must not exceed the available quantity on that DeliveryItem
/// (QtyDelivered minus all previously recorded distributions).
/// </summary>
public sealed record CreateStandaloneDistributionDto
{
    public required Guid     DeliveryItemId { get; init; }
    public required string   Division       { get; init; }
    public required decimal  QtyIssued      { get; init; }
    public required DateOnly DateIssued     { get; init; }
    public required string   IssuedBy       { get; init; }
    public string?           Remarks        { get; init; }
}
