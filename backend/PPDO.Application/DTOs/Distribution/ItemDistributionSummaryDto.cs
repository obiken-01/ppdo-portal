namespace PPDO.Application.DTOs.Distribution;

/// <summary>
/// Full distribution breakdown for one catalog item —
/// returned by GET /api/distributions/item/{stockNo}.
/// </summary>
public sealed record ItemDistributionSummaryDto(
    string   StockNo,
    string   Description,
    string?  Category,
    string   Unit,
    decimal  TotalOrdered,
    decimal  TotalDelivered,
    decimal  TotalDistributed,
    decimal  OnHand,
    IReadOnlyList<DeliveryItemBreakdownDto> DeliveryItems);

/// <summary>
/// One stock source that contained this item, with its existing distributions —
/// either a delivery batch or the warehouse-count pool (RAL-223).
/// QtyAvailable = QtyDelivered - QtyDistributed (undistributed stock from this source).
/// Exactly one of DeliveryItemId / StockBalanceId is set, matching <see cref="Source"/>.
/// </summary>
public sealed record DeliveryItemBreakdownDto(
    Guid?    DeliveryItemId,
    Guid?    StockBalanceId,
    string   Source,   // "Delivery" | "Warehouse Count"
    string   DeliveryRef,
    DateOnly DeliveryDate,
    Guid     PRId,
    string   PRNo,
    decimal  QtyDelivered,
    decimal  QtyDistributed,
    decimal  QtyAvailable,
    IReadOnlyList<ExistingDistributionDto> Distributions);

/// <summary>One already-recorded distribution within a stock source.</summary>
public sealed record ExistingDistributionDto(
    Guid     Id,
    string   IssueRef,
    string   Division,
    decimal  QtyIssued,
    DateOnly DateIssued,
    string   IssuedBy,
    string?  Remarks);

/// <summary>
/// Response returned after a distribution is successfully created. Exactly one of
/// DeliveryItemId / StockBalanceId is set, matching <see cref="Source"/> (RAL-223).
/// </summary>
public sealed record DistributionCreatedDto(
    Guid     Id,
    string   IssueRef,
    Guid?    DeliveryItemId,
    Guid?    StockBalanceId,
    string   Source,   // "Delivery" | "Warehouse Count"
    string   DeliveryRef,
    string   PRNo,
    string   StockNo,
    string   Description,
    string   Division,
    decimal  QtyIssued,
    DateOnly DateIssued,
    string   IssuedBy,
    string?  Remarks);
