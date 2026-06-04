using PPDO.Application.DTOs.Inventory;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Contract for Inventory Dashboard stats and Item Ledger queries.
/// All methods apply division scope for Staff/Observer automatically.
/// </summary>
public interface IInventoryService
{
    /// <summary>
    /// Returns the two grouped stat panels for the Inventory Dashboard.
    /// Group 1: PR counts by status.
    /// Group 2: Stock alerts (in stock vs low/out), total PR value, unique items tracked.
    /// Staff/Observer: scoped to their own division's PRs.
    /// </summary>
    Task<InventoryStatsDto> GetStatsAsync(
        User requester,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the Item Ledger — running stock totals per catalog item.
    /// Each row aggregates QtyOrdered, QtyDelivered, QtyDistributed across all PRs.
    /// OnHand = QtyDelivered - QtyDistributed.
    /// Staff/Observer: only items appearing in their division's PRs are included.
    /// Items with no PR activity are excluded (nothing to track yet).
    /// </summary>
    /// <param name="deliveryDateFrom">
    /// If set together with <paramref name="deliveryDateTo"/>, only items that had
    /// at least one delivery within the date range are included.
    /// All-time quantity totals are still shown for the matching items.
    /// </param>
    Task<IReadOnlyList<ItemLedgerRowDto>> GetItemLedgerAsync(
        User requester,
        DateOnly? deliveryDateFrom = null,
        DateOnly? deliveryDateTo   = null,
        CancellationToken cancellationToken = default);
}
