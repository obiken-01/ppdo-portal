using PPDO.Domain.Entities;

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
}
