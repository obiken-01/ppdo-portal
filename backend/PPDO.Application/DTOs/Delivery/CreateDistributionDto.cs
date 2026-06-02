using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.Delivery;

/// <summary>
/// A single per-division distribution line within a delivery item.
/// Enables split delivery: one delivered batch issued to multiple divisions.
/// Sum of all QtyIssued values for a DeliveryItem must equal QtyDelivered.
/// </summary>
public sealed record CreateDistributionDto
{
    public required Division Division { get; init; }
    public required decimal QtyIssued { get; init; }
    public required DateOnly DateIssued { get; init; }
    public required string IssuedBy { get; init; }
    public string? Remarks { get; init; }
}
