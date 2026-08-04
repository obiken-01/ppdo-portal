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

/// <summary>Paged delivery list — GET /api/deliveries?page=&amp;pageSize= response.</summary>
public sealed record DeliveryPagedResultDto(
    IReadOnlyList<DeliverySummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize);
