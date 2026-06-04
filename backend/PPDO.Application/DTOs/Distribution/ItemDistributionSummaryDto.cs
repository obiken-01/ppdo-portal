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
/// One delivery batch that contained this item, with its existing distributions.
/// QtyAvailable = QtyDelivered - QtyDistributed (undistributed stock from this batch).
/// </summary>
public sealed record DeliveryItemBreakdownDto(
    Guid     DeliveryItemId,
    string   DeliveryRef,
    DateOnly DeliveryDate,
    Guid     PRId,
    string   PRNo,
    decimal  QtyDelivered,
    decimal  QtyDistributed,
    decimal  QtyAvailable,
    IReadOnlyList<ExistingDistributionDto> Distributions);

/// <summary>One already-recorded distribution within a delivery batch.</summary>
public sealed record ExistingDistributionDto(
    Guid     Id,
    string   IssueRef,
    string   Division,
    decimal  QtyIssued,
    DateOnly DateIssued,
    string   IssuedBy,
    string?  Remarks);

/// <summary>Response returned after a distribution is successfully created.</summary>
public sealed record DistributionCreatedDto(
    Guid     Id,
    string   IssueRef,
    Guid     DeliveryItemId,
    string   DeliveryRef,
    string   PRNo,
    string   StockNo,
    string   Description,
    string   Division,
    decimal  QtyIssued,
    DateOnly DateIssued,
    string   IssuedBy,
    string?  Remarks);
