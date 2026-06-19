using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Distribution;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Distribution business logic — records how received goods are issued to divisions.
///
/// Flow:
///   1. Goods arrive → POST /api/deliveries (records Delivery + DeliveryItems, no distributions)
///   2. Goods are issued → POST /api/distributions (records who got what from which batch)
///
/// IssueRef format: ISS-YYYYMMDD-XXXXX-1  (XXXXX = 5-char random, Manila time)
/// </summary>
public sealed class DistributionService : IDistributionService
{
    private readonly IDeliveryRepository     _deliveries;
    private readonly IItemMasterRepository   _items;
    private readonly IPermissionService      _permissions;
    private readonly IRepository<Distribution> _distributions;
    private readonly ILogger<DistributionService> _logger;

    private static readonly char[] RefChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private static readonly TimeZoneInfo ManilaZone = LoadManilaZone();

    private static TimeZoneInfo LoadManilaZone()
    {
        try   { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }
    }

    public DistributionService(
        IDeliveryRepository deliveries,
        IItemMasterRepository items,
        IPermissionService permissions,
        IRepository<Distribution> distributions,
        ILogger<DistributionService> logger)
    {
        _deliveries    = deliveries;
        _items         = items;
        _permissions   = permissions;
        _distributions = distributions;
        _logger        = logger;
    }

    // ── GetItemSummaryAsync ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<ItemDistributionSummaryDto>> GetItemSummaryAsync(
        User requester,
        string stockNo,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to view distribution summary without CanAccessInventory.",
                requester.Id);
            return ServiceResult<ItemDistributionSummaryDto>.Forbidden(
                "You do not have permission to access Inventory.");
        }

        DivisionScope scope = DivisionScope.Resolve(requester);

        // Office users (Staff/Observer with no division) have no inventory scope —
        // never fall through to an unscoped (all-divisions) query.
        if (scope.SeeNothing)
            return ServiceResult<ItemDistributionSummaryDto>.NotFound(
                $"No activity found for StockNo '{stockNo}'.");

        Division? scopeDivision = scope.Division;

        // Load catalog entry for item details (optional — might be an orphan StockNo).
        ItemMaster? master = await _items.GetByStockNoAsync(stockNo, cancellationToken);

        // Load all delivery batches for this item.
        IReadOnlyList<DeliveryItemBreakdownRow> batches =
            await _deliveries.GetDeliveryItemBreakdownsByStockNoAsync(stockNo, scopeDivision, cancellationToken);

        if (batches.Count == 0 && master is null)
            return ServiceResult<ItemDistributionSummaryDto>.NotFound(
                $"No activity found for StockNo '{stockNo}'.");

        // Aggregate totals.
        decimal totalDelivered    = batches.Sum(b => b.QtyDelivered);
        decimal totalDistributed  = batches.Sum(b => b.Distributions.Sum(d => d.QtyIssued));
        decimal totalOrdered      = 0m; // would require separate query — excluded from this view

        IReadOnlyList<DeliveryItemBreakdownDto> batchDtos = batches
            .Select(b =>
            {
                decimal distributed = b.Distributions.Sum(d => d.QtyIssued);
                return new DeliveryItemBreakdownDto(
                    DeliveryItemId: b.DeliveryItemId,
                    DeliveryRef:    b.DeliveryRef,
                    DeliveryDate:   b.DeliveryDate,
                    PRId:           b.PRId,
                    PRNo:           b.PRNo,
                    QtyDelivered:   b.QtyDelivered,
                    QtyDistributed: distributed,
                    QtyAvailable:   Math.Max(0, b.QtyDelivered - distributed),
                    Distributions:  b.Distributions
                        .Select(d => new ExistingDistributionDto(
                            d.Id, d.IssueRef, d.Division.ToString(),
                            d.QtyIssued, d.DateIssued, d.IssuedBy, d.Remarks))
                        .ToList());
            })
            .ToList();

        return ServiceResult<ItemDistributionSummaryDto>.Ok(new ItemDistributionSummaryDto(
            StockNo:          stockNo,
            Description:      master?.Description ?? stockNo,
            Category:         master?.Category,
            Unit:             master?.Unit ?? "—",
            TotalOrdered:     totalOrdered,
            TotalDelivered:   totalDelivered,
            TotalDistributed: totalDistributed,
            OnHand:           Math.Max(0, totalDelivered - totalDistributed),
            DeliveryItems:    batchDtos));
    }

    // ── CreateAsync ────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<DistributionCreatedDto>> CreateAsync(
        User requester,
        CreateStandaloneDistributionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a distribution without CanAccessInventory.",
                requester.Id);
            return ServiceResult<DistributionCreatedDto>.Forbidden(
                "You do not have permission to access Inventory.");
        }

        if (!Enum.TryParse<Division>(dto.Division, ignoreCase: true, out Division division))
            return ServiceResult<DistributionCreatedDto>.BadRequest(
                $"Invalid division '{dto.Division}'. Must be one of: Admin, Planning, RM, MIS, SPD.");

        if (dto.QtyIssued <= 0)
            return ServiceResult<DistributionCreatedDto>.BadRequest(
                "QtyIssued must be greater than zero.");

        // Load the delivery item with its existing distributions.
        DeliveryItemBreakdownRow? breakdown =
            await _deliveries.GetDeliveryItemBreakdownAsync(dto.DeliveryItemId, cancellationToken);

        if (breakdown is null)
            return ServiceResult<DistributionCreatedDto>.NotFound(
                $"DeliveryItem {dto.DeliveryItemId} not found.");

        decimal alreadyDistributed = breakdown.Distributions.Sum(d => d.QtyIssued);
        decimal available          = breakdown.QtyDelivered - alreadyDistributed;

        if (dto.QtyIssued > available)
            return ServiceResult<DistributionCreatedDto>.BadRequest(
                $"QtyIssued ({dto.QtyIssued}) exceeds available quantity ({available}) for this delivery batch.");

        // Generate IssueRef: ISS-YYYYMMDD-XXXXX-1
        DateTime manilaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaZone);
        string   suffix    = new(Enumerable.Range(0, 5)
                                    .Select(_ => RefChars[Random.Shared.Next(RefChars.Length)])
                                    .ToArray());
        string issueRef = $"ISS-{manilaNow:yyyyMMdd}-{suffix}-1";

        Distribution dist = new()
        {
            Id             = Guid.NewGuid(),
            IssueRef       = issueRef,
            DeliveryItemId = dto.DeliveryItemId,
            Division       = division,
            QtyIssued      = dto.QtyIssued,
            DateIssued     = dto.DateIssued,
            IssuedBy       = dto.IssuedBy.Trim(),
            Remarks        = dto.Remarks?.Trim(),
        };

        await _distributions.AddAsync(dist, cancellationToken);
        await _distributions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Distribution recorded. IssueRef: {IssueRef}, DeliveryRef: {DeliveryRef}, Division: {Division}, QtyIssued: {QtyIssued}, IssuedBy: {IssuedBy}, UserId: {UserId}",
            issueRef, breakdown.DeliveryRef, division, dto.QtyIssued, dto.IssuedBy, requester.Id);

        return ServiceResult<DistributionCreatedDto>.Ok(new DistributionCreatedDto(
            Id:             dist.Id,
            IssueRef:       issueRef,
            DeliveryItemId: dto.DeliveryItemId,
            DeliveryRef:    breakdown.DeliveryRef,
            PRNo:           breakdown.PRNo,
            StockNo:        "—",   // requester can get this from context
            Description:    "—",
            Division:       division.ToString(),
            QtyIssued:      dto.QtyIssued,
            DateIssued:     dto.DateIssued,
            IssuedBy:       dto.IssuedBy.Trim(),
            Remarks:        dto.Remarks?.Trim()));
    }
}
