using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Delivery;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Receive Delivery business logic — DeliveryRef and IssueRef generation,
/// split-delivery validation, and automatic PR status recalculation.
///
/// DeliveryRef format: DEL-YYYYMMDD-XXXXX  (5-char uppercase alphanumeric random, Manila time)
/// IssueRef format:    ISS-YYYYMMDD-XXXXX-N (same random suffix shared per delivery, N = 1-based counter)
/// PR status rule: Open → PartiallyDelivered → FullyDelivered based on cumulative qty delivered vs ordered.
/// </summary>
public sealed class DeliveryService : IDeliveryService
{
    private readonly IDeliveryRepository        _deliveries;
    private readonly IPurchaseRequestRepository _prs;
    private readonly IPermissionService         _permissions;
    private readonly ILogger<DeliveryService>   _logger;

    // Characters used for random ref suffixes — uppercase alphanumeric, no ambiguous O/0/I/1.
    private static readonly char[] RefChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    // Manila is UTC+8.
    private static readonly TimeZoneInfo ManilaZone = LoadManilaZone();

    private static TimeZoneInfo LoadManilaZone()
    {
        try   { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }
    }

    public DeliveryService(
        IDeliveryRepository deliveries,
        IPurchaseRequestRepository prs,
        IPermissionService permissions,
        ILogger<DeliveryService> logger)
    {
        _deliveries  = deliveries;
        _prs         = prs;
        _permissions = permissions;
        _logger      = logger;
    }

    // ── GetAllAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeliverySummaryDto>> GetAllAsync(
        User requester,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PurchaseRequest> prs;

        if (requester.Role is UserRole.Staff or UserRole.Observer)
            prs = await _prs.GetByDivisionAsync(requester.Division, cancellationToken);
        else
            prs = await _prs.GetAllAsync(cancellationToken);

        List<DeliverySummaryDto> result = new();

        foreach (PurchaseRequest pr in prs)
        {
            IReadOnlyList<Delivery> deliveries =
                await _deliveries.GetByPRIdAsync(pr.Id, cancellationToken);

            result.AddRange(deliveries.Select(MapToSummary));
        }

        return result.OrderByDescending(d => d.DeliveryDate).ToList();
    }

    // ── GetByPRIdAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<DeliverySummaryDto>>> GetByPRIdAsync(
        User requester,
        Guid prId,
        CancellationToken cancellationToken = default)
    {
        PurchaseRequest? pr = await _prs.GetByIdAsync(prId, cancellationToken);
        if (pr is null)
            return ServiceResult<IReadOnlyList<DeliverySummaryDto>>.NotFound(
                $"Purchase Request {prId} not found.");

        if (requester.Role is UserRole.Staff or UserRole.Observer
            && pr.Division != requester.Division)
            return ServiceResult<IReadOnlyList<DeliverySummaryDto>>.Forbidden(
                "You can only view deliveries for your own division.");

        IReadOnlyList<Delivery> deliveries =
            await _deliveries.GetByPRIdAsync(prId, cancellationToken);

        return ServiceResult<IReadOnlyList<DeliverySummaryDto>>.Ok(
            deliveries.Select(MapToSummary).ToList());
    }

    // ── GetByIdAsync ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<DeliveryResponseDto>> GetByIdAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        Delivery? delivery = await _deliveries.GetByIdWithItemsAsync(id, cancellationToken);
        if (delivery is null)
            return ServiceResult<DeliveryResponseDto>.NotFound($"Delivery {id} not found.");

        // Verify division scope — load the PR to check its division.
        if (requester.Role is UserRole.Staff or UserRole.Observer)
        {
            PurchaseRequest? pr = await _prs.GetByIdAsync(delivery.PRId, cancellationToken);
            if (pr is not null && pr.Division != requester.Division)
            {
                _logger.LogWarning(
                    "Permission denied — user {UserId} attempted to view delivery {DeliveryRef} for division {Division}.",
                    requester.Id, delivery.DeliveryRef, pr.Division);
                return ServiceResult<DeliveryResponseDto>.Forbidden(
                    "You can only view deliveries for your own division.");
            }
        }

        return ServiceResult<DeliveryResponseDto>.Ok(MapToResponse(delivery));
    }

    // ── CreateAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<DeliveryResponseDto>> CreateAsync(
        User requester,
        CreateDeliveryDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to submit a delivery without CanAccessInventory.",
                requester.Id);
            return ServiceResult<DeliveryResponseDto>.Forbidden(
                "You do not have permission to receive deliveries.");
        }

        // Load the PR with its items first — gives a proper 404 before any other check.
        PurchaseRequest? pr = await _prs.GetWithItemsAsync(dto.PRId, cancellationToken);
        if (pr is null)
            return ServiceResult<DeliveryResponseDto>.NotFound(
                $"Purchase Request {dto.PRId} not found.");

        if (pr.Status == PRStatus.Completed)
            return ServiceResult<DeliveryResponseDto>.BadRequest(
                $"PR {pr.PRNo} is already Completed and cannot receive further deliveries.");

        // Division scope — Staff can only deliver against their own division's PRs.
        if (requester.Role is UserRole.Staff or UserRole.Observer
            && pr.Division != requester.Division)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to deliver against PR {PRNo} from division {Division}.",
                requester.Id, pr.PRNo, pr.Division);
            return ServiceResult<DeliveryResponseDto>.Forbidden(
                "You can only receive deliveries for your own division's Purchase Requests.");
        }

        // Validate items list.
        if (dto.Items is null || dto.Items.Count == 0)
            return ServiceResult<DeliveryResponseDto>.BadRequest(
                "A delivery must have at least one item.");

        // Validate each delivery item.
        HashSet<Guid> prItemIds = pr.Items.Select(i => i.Id).ToHashSet();

        foreach (CreateDeliveryItemDto item in dto.Items)
        {
            if (!prItemIds.Contains(item.PRItemId))
                return ServiceResult<DeliveryResponseDto>.BadRequest(
                    $"PRItemId {item.PRItemId} does not belong to PR {pr.PRNo}.");

            if (item.QtyDelivered <= 0)
                return ServiceResult<DeliveryResponseDto>.BadRequest(
                    $"QtyDelivered must be greater than zero for PRItemId {item.PRItemId}.");

            // If inline distributions are provided, their qty sum must match QtyDelivered exactly.
            if (item.Distributions is { Count: > 0 })
            {
                decimal distSum = item.Distributions.Sum(d => d.QtyIssued);
                if (distSum != item.QtyDelivered)
                    return ServiceResult<DeliveryResponseDto>.BadRequest(
                        $"Distribution quantities ({distSum}) do not match QtyDelivered ({item.QtyDelivered}) " +
                        $"for PRItemId {item.PRItemId}. They must be equal.");
            }
        }

        // Generate DeliveryRef — retry on collision (statistically negligible).
        DateTime manilaNow   = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaZone);
        string deliveryRef   = await GenerateUniqueDeliveryRefAsync(manilaNow, cancellationToken);
        string randomSuffix  = deliveryRef[^5..]; // reuse same suffix for IssueRefs
        DateTime utcNow      = DateTime.UtcNow;

        // Build entity graph.
        int issueSeq = 1; // global counter across all distributions in this delivery

        Delivery delivery = new()
        {
            Id          = Guid.NewGuid(),
            DeliveryRef = deliveryRef,
            PRId        = dto.PRId,
            DeliveryDate = dto.DeliveryDate,
            ReceivedBy  = dto.ReceivedBy.Trim(),
            Supplier    = dto.Supplier?.Trim(),
            Remarks     = dto.Remarks?.Trim(),
            CreatedAt   = utcNow,
        };

        foreach (CreateDeliveryItemDto itemDto in dto.Items)
        {
            DeliveryItem deliveryItem = new()
            {
                Id           = Guid.NewGuid(),
                DeliveryId   = delivery.Id,
                PRItemId     = itemDto.PRItemId,
                QtyDelivered = itemDto.QtyDelivered,
            };

            foreach (CreateDistributionDto distDto in itemDto.Distributions)
            {
                // Parse Division string → enum (matches CreateUserDto / CreatePRDto pattern).
                if (!Enum.TryParse<Division>(distDto.Division, ignoreCase: true, out Division distDivision))
                    return ServiceResult<DeliveryResponseDto>.BadRequest(
                        $"Invalid division '{distDto.Division}' in distribution. " +
                        "Must be one of: Admin, Planning, RM, MIS, SPD.");

                string issueRef = BuildIssueRef(manilaNow, randomSuffix, issueSeq++);

                deliveryItem.Distributions.Add(new Distribution
                {
                    Id             = Guid.NewGuid(),
                    IssueRef       = issueRef,
                    DeliveryItemId = deliveryItem.Id,
                    Division       = distDivision,
                    QtyIssued      = distDto.QtyIssued,
                    DateIssued     = distDto.DateIssued,
                    IssuedBy       = distDto.IssuedBy.Trim(),
                    Remarks        = distDto.Remarks?.Trim(),
                });
            }

            delivery.Items.Add(deliveryItem);
        }

        await _deliveries.AddAsync(delivery, cancellationToken);

        // Recalculate PR status before saving — both the delivery and status update
        // are in the same SaveChanges call (one transaction).
        await RecalculatePRStatusAsync(pr, delivery, cancellationToken);

        await _deliveries.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Delivery received. DeliveryRef: {DeliveryRef}, PRNo: {PRNo}, UserId: {UserId}",
            delivery.DeliveryRef, pr.PRNo, requester.Id);

        return ServiceResult<DeliveryResponseDto>.Ok(MapToResponse(delivery));
    }

    // ── PR status recalculation ────────────────────────────────────────────────

    /// <summary>
    /// Recalculates PR status after a delivery is submitted.
    ///
    /// Algorithm:
    ///   1. Load total qty delivered per PRItem across ALL prior deliveries.
    ///   2. Add the quantities from the new (not yet saved) delivery being submitted.
    ///   3. Compare cumulative totals against each PRItem.Quantity.
    ///   4. If all items fully covered → FullyDelivered.
    ///      Else any item has delivery → PartiallyDelivered.
    ///
    /// The PR entity is mutated in-place — the caller's SaveChangesAsync persists it.
    /// </summary>
    private async Task RecalculatePRStatusAsync(
        PurchaseRequest pr,
        Delivery newDelivery,
        CancellationToken cancellationToken)
    {
        PRStatus oldStatus = pr.Status;

        // Quantities already saved in the DB from prior deliveries.
        Dictionary<Guid, decimal> priorDelivered =
            await _deliveries.GetTotalDeliveredByPRAsync(pr.Id, cancellationToken);

        // Add quantities from the delivery we are about to save.
        foreach (DeliveryItem item in newDelivery.Items)
        {
            priorDelivered.TryGetValue(item.PRItemId, out decimal existing);
            priorDelivered[item.PRItemId] = existing + item.QtyDelivered;
        }

        // Determine new status.
        bool anyDelivered  = false;
        bool allFullyMet   = true;

        foreach (PRItem prItem in pr.Items)
        {
            priorDelivered.TryGetValue(prItem.Id, out decimal totalDelivered);

            if (totalDelivered > 0)
                anyDelivered = true;

            if (totalDelivered < prItem.Quantity)
                allFullyMet = false;
        }

        PRStatus newStatus = (anyDelivered, allFullyMet) switch
        {
            (true, true)  => PRStatus.FullyDelivered,
            (true, false) => PRStatus.PartiallyDelivered,
            _             => PRStatus.Open,
        };

        if (newStatus != oldStatus)
        {
            pr.Status = newStatus;
            await _prs.UpdateAsync(pr, cancellationToken);

            _logger.LogInformation(
                "PR status changed. PRNo: {PRNo}, OldStatus: {OldStatus}, NewStatus: {NewStatus}",
                pr.PRNo, oldStatus, newStatus);
        }
    }

    // ── Ref generation ─────────────────────────────────────────────────────────

    /// <summary>Generates DEL-YYYYMMDD-XXXXX, retrying until the ref is unique.</summary>
    private async Task<string> GenerateUniqueDeliveryRefAsync(
        DateTime manilaNow,
        CancellationToken cancellationToken)
    {
        string date = manilaNow.ToString("yyyyMMdd");

        while (true)
        {
            string candidate = $"DEL-{date}-{GenerateRandomSuffix()}";
            bool exists = await _deliveries.DeliveryRefExistsAsync(candidate, cancellationToken);
            if (!exists)
                return candidate;
        }
    }

    /// <summary>Builds ISS-YYYYMMDD-XXXXX-N using the delivery's shared random suffix.</summary>
    private static string BuildIssueRef(DateTime manilaNow, string sharedSuffix, int seq)
        => $"ISS-{manilaNow:yyyyMMdd}-{sharedSuffix}-{seq}";

    /// <summary>Generates a 5-character uppercase alphanumeric random string.</summary>
    private static string GenerateRandomSuffix()
        => new(Enumerable.Range(0, 5)
            .Select(_ => RefChars[Random.Shared.Next(RefChars.Length)])
            .ToArray());

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static DeliverySummaryDto MapToSummary(Delivery d) => new(
        d.Id, d.DeliveryRef, d.PRId, d.DeliveryDate,
        d.ReceivedBy, d.Supplier, d.CreatedAt);

    private static DeliveryResponseDto MapToResponse(Delivery d) => new(
        d.Id, d.DeliveryRef, d.PRId, d.DeliveryDate,
        d.ReceivedBy, d.Supplier, d.Remarks, d.CreatedAt,
        d.Items.Select(i => new DeliveryItemDto(
            i.Id, i.DeliveryId, i.PRItemId, i.QtyDelivered,
            i.Distributions.Select(dist => new DistributionDto(
                dist.Id, dist.IssueRef, dist.DeliveryItemId,
                dist.Division, dist.QtyIssued, dist.DateIssued,
                dist.IssuedBy, dist.Remarks))
            .ToList()))
        .ToList());
}
