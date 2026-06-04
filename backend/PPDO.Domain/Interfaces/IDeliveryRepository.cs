using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Delivery-specific data access contract.
/// Extends <see cref="IRepository{T}"/> with domain queries needed by
/// DeliveryService — list by PR, load with nested items/distributions,
/// and aggregate delivered quantities for PR status recalculation.
///
/// All methods are async and support CancellationToken.
/// Include chains never exceed depth 2 (Delivery → Items → Distributions).
/// </summary>
public interface IDeliveryRepository : IRepository<Delivery>
{
    /// <summary>
    /// Returns all deliveries for the given PR, ordered by DeliveryDate descending.
    /// No nested includes — used for summary list views.
    /// </summary>
    Task<IReadOnlyList<Delivery>> GetByPRIdAsync(
        Guid prId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the delivery with its Items and each item's Distributions eager-loaded.
    /// Include depth: 2 (Delivery → Items → Distributions).
    /// Used for delivery detail and PR Report Section 3.
    /// </summary>
    Task<Delivery?> GetByIdWithItemsAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the total quantity delivered per PRItem across all deliveries for the given PR.
    /// Used by DeliveryService to recalculate PR status after each submission.
    /// Key = PRItemId, Value = sum of QtyDelivered across all DeliveryItems for that PRItem.
    /// </summary>
    Task<Dictionary<Guid, decimal>> GetTotalDeliveredByPRAsync(
        Guid prId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true if a delivery with the given DeliveryRef already exists.
    /// Used to guarantee uniqueness before inserting (collision guard for random suffix).
    /// </summary>
    Task<bool> DeliveryRefExistsAsync(
        string deliveryRef,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all deliveries for a PR with Items, Distributions, and PRItem
    /// eager-loaded — the full graph needed by PR Report Section 3 and ExcelService.ExportPRReport.
    /// Include depth: 2 per chain (two sibling ThenIncludes on Items).
    /// </summary>
    Task<IReadOnlyList<Delivery>> GetDeliveriesForPRReportAsync(
        Guid prId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every DeliveryItem for the given StockNo, with its parent Delivery,
    /// PR number, and existing Distributions — all the data needed to render the
    /// Distribution page breakdown for one item.
    /// Optionally scoped to a division for Staff/Observer.
    /// </summary>
    Task<IReadOnlyList<DeliveryItemBreakdownRow>> GetDeliveryItemBreakdownsByStockNoAsync(
        string stockNo,
        Division? division = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a single DeliveryItem with its Distributions loaded.</summary>
    Task<DeliveryItemBreakdownRow?> GetDeliveryItemBreakdownAsync(
        Guid deliveryItemId,
        CancellationToken cancellationToken = default);
}

// ---------------------------------------------------------------------------
// Projection records used by breakdown queries (not full EF entities)
// ---------------------------------------------------------------------------

/// <summary>All data needed to render one delivery batch row on the Distribution page.</summary>
public sealed record DeliveryItemBreakdownRow(
    Guid          DeliveryItemId,
    string        DeliveryRef,
    DateOnly      DeliveryDate,
    Guid          PRId,
    string        PRNo,
    decimal       QtyDelivered,
    IReadOnlyList<DistributionBreakdownRow> Distributions);

/// <summary>One existing distribution record within a delivery item breakdown.</summary>
public sealed record DistributionBreakdownRow(
    Guid     Id,
    string   IssueRef,
    Division Division,
    decimal  QtyIssued,
    DateOnly DateIssued,
    string   IssuedBy,
    string?  Remarks);
