using PPDO.Application.Common;
using PPDO.Application.DTOs.Distribution;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Contract for the Distribution feature — separate from Receive Delivery.
///
/// After a delivery is recorded (POST /api/deliveries), distributions are
/// created here to record which division received which items from each batch.
/// </summary>
public interface IDistributionService
{
    /// <summary>
    /// Returns the full distribution breakdown for a catalog item:
    /// item details, all delivery batches it appeared in, and existing
    /// distributions per batch. QtyAvailable per batch = QtyDelivered - distributed.
    /// Staff/Observer: scoped to their own division's deliveries.
    /// Returns NotFound if no delivery activity exists for the StockNo.
    /// </summary>
    Task<ServiceResult<ItemDistributionSummaryDto>> GetItemSummaryAsync(
        User requester,
        string stockNo,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Allocates each split in <paramref name="dto"/> across the item's available delivery
    /// batches — FIFO, oldest DeliveryDate first — and creates one Distribution record per
    /// (split × batch) pair drawn from. Validates:
    ///   - At least one split, each with QtyIssued > 0, a non-blank IssuedBy, and a
    ///     Division that resolves to an active configured division
    ///   - Total requested quantity across all splits does not exceed total available stock
    ///     for the StockNo (scoped to the requester's division for Staff)
    /// Generates one IssueRef (ISS-YYYYMMDD-XXXXX-N) per created Distribution, sharing a
    /// random suffix across the whole allocation with a running sequence number.
    /// Requires CanAccessInventory. All created Distributions are saved in one transaction.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<DistributionCreatedDto>>> AllocateAsync(
        User requester,
        string stockNo,
        CreateItemDistributionDto dto,
        CancellationToken cancellationToken = default);
}
