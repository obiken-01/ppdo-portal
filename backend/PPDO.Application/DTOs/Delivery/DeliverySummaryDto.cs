namespace PPDO.Application.DTOs.Delivery;

/// <summary>Lightweight delivery record for list views (no items collection).</summary>
public sealed record DeliverySummaryDto(
    Guid Id,
    string DeliveryRef,
    Guid PRId,
    DateOnly DeliveryDate,
    string ReceivedBy,
    string? Supplier,
    DateTime CreatedAt);
