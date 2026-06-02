namespace PPDO.Application.DTOs.Delivery;

/// <summary>Full delivery detail — includes all item lines and distributions.</summary>
public sealed record DeliveryResponseDto(
    Guid Id,
    string DeliveryRef,
    Guid PRId,
    DateOnly DeliveryDate,
    string ReceivedBy,
    string? Supplier,
    string? Remarks,
    DateTime CreatedAt,
    IReadOnlyList<DeliveryItemDto> Items);
