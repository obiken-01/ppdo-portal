using PPDO.Application.Common;
using PPDO.Application.DTOs.Delivery;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Contract for all Receive Delivery business logic.
/// DeliveryRef generation, PR status recalculation, split-delivery aggregation,
/// and IssueRef generation all live here — never in the Function handler.
/// </summary>
public interface IDeliveryService
{
    /// <summary>
    /// Returns all deliveries visible to the requester.
    /// Staff/Observer: own division's PRs only. Admin/SuperAdmin: all.
    /// </summary>
    Task<IReadOnlyList<DeliverySummaryDto>> GetAllAsync(
        User requester,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all deliveries recorded against a specific PR.
    /// Enforces division-scope for Staff/Observer.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<DeliverySummaryDto>>> GetByPRIdAsync(
        User requester,
        Guid prId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full delivery detail — items and per-division distributions.
    /// Returns Forbidden if a Staff/Observer tries to view another division's PR delivery.
    /// </summary>
    Task<ServiceResult<DeliveryResponseDto>> GetByIdAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a delivery against a PR.
    ///
    /// Business rules enforced:
    ///   - PR must exist and not be Completed.
    ///   - Each PRItemId must belong to the target PR.
    ///   - QtyDelivered must be > 0 for each item.
    ///   - Sum of Distribution.QtyIssued must equal QtyDelivered for each item.
    ///   - Staff can only deliver against their own division's PRs.
    ///
    /// After saving:
    ///   - PR status is recalculated: Open → PartiallyDelivered → FullyDelivered.
    ///   - DeliveryRef generated as DEL-YYYYMMDD-XXXXX (Manila time, 5-char random).
    ///   - IssueRef generated per Distribution as ISS-YYYYMMDD-XXXXX-N (shared random, N = 1-based global counter within the delivery).
    /// </summary>
    Task<ServiceResult<DeliveryResponseDto>> CreateAsync(
        User requester,
        CreateDeliveryDto dto,
        CancellationToken cancellationToken = default);
}
