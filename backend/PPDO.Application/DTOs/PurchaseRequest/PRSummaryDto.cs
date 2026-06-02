using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>Lightweight PR record for list views (no items collection).</summary>
public sealed record PRSummaryDto(
    Guid Id,
    string PRNo,
    DateOnly PRDate,
    Division Division,
    string RequestedBy,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAt);
