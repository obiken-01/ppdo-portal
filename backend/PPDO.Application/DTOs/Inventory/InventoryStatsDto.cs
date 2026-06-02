namespace PPDO.Application.DTOs.Inventory;

/// <summary>
/// Inventory Dashboard stat cards — two grouped panels.
/// Matches the layout in PPDO_PROJECT_CONTEXT.md Section 10.
/// </summary>
public sealed record InventoryStatsDto(
    PRStatsGroupDto PurchaseRequests,
    AlertsGroupDto InventoryAlerts);

/// <summary>
/// Group 1 — PURCHASE REQUESTS stat cards.
/// FullyDeliveredOrCompleted covers both FullyDelivered and Completed statuses —
/// both represent PRs that have been fully fulfilled.
/// </summary>
public sealed record PRStatsGroupDto(
    int Total,
    int Open,
    int PartiallyDelivered,
    int FullyDeliveredOrCompleted);

/// <summary>
/// Group 2 — INVENTORY ALERTS stat cards.
/// InStock       = items where OnHand > ReorderQty
/// LowOrOutOfStock = items where OnHand ≤ ReorderQty (includes zero)
/// TotalPRValue  = sum of TotalAmount across all visible PRs
/// UniqueItemsTracked = count of ItemMaster entries visible to the requester
/// </summary>
public sealed record AlertsGroupDto(
    int InStock,
    int LowOrOutOfStock,
    decimal TotalPRValue,
    int UniqueItemsTracked);
