using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.Delivery;

/// <summary>Response DTO for a single per-division distribution record.</summary>
public sealed record DistributionDto(
    Guid Id,
    string IssueRef,
    Guid DeliveryItemId,
    Division Division,
    decimal QtyIssued,
    DateOnly DateIssued,
    string IssuedBy,
    string? Remarks);
