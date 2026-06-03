namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>Lightweight PR record for list views (no items collection).</summary>
public sealed record PRSummaryDto(
    Guid Id,
    string PRNo,
    DateOnly PRDate,
    /// <summary>Division name string e.g. "Admin" — not the enum integer.</summary>
    string Division,
    string RequestedBy,
    decimal TotalAmount,
    string Status,
    DateTime CreatedAt);
