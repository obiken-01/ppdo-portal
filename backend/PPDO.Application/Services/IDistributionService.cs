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
    /// Creates a distribution record against a specific DeliveryItem.
    /// Validates:
    ///   - DeliveryItem exists and belongs to requester's scope
    ///   - QtyIssued > 0
    ///   - QtyIssued does not exceed QtyAvailable for that batch
    /// Generates IssueRef (ISS-YYYYMMDD-XXXXX-1).
    /// Requires CanAccessInventory.
    /// </summary>
    Task<ServiceResult<DistributionCreatedDto>> CreateAsync(
        User requester,
        CreateStandaloneDistributionDto dto,
        CancellationToken cancellationToken = default);
}
