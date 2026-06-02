namespace PPDO.Application.DTOs.Delivery;

/// <summary>Response DTO for a single item line within a delivery.</summary>
public sealed record DeliveryItemDto(
    Guid Id,
    Guid DeliveryId,
    Guid PRItemId,
    decimal QtyDelivered,
    IReadOnlyList<DistributionDto> Distributions);
