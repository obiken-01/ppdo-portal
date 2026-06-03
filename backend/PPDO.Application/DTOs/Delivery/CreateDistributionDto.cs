namespace PPDO.Application.DTOs.Delivery;

/// <summary>
/// A single per-division distribution line within a delivery item.
/// Division is accepted as a string name (e.g. "Admin") — parsed to the Division enum
/// in DeliveryService, matching the pattern used by CreateUserDto and CreatePRDto.
/// Sum of all QtyIssued values for a DeliveryItem must equal QtyDelivered.
/// </summary>
public sealed record CreateDistributionDto
{
    /// <summary>"Admin" | "Planning" | "RM" | "MIS" | "SPD"</summary>
    public required string Division { get; init; }
    public required decimal QtyIssued { get; init; }
    public required DateOnly DateIssued { get; init; }
    public required string IssuedBy { get; init; }
    public string? Remarks { get; init; }
}
