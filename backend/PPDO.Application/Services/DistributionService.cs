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
    private readonly IRepository<Division>    _divisions;
    private readonly IAuditService           _audit;
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
        IRepository<Division> divisions,
        IAuditService audit,
        ILogger<DistributionService> logger)
    {
        _deliveries    = deliveries;
        _items         = items;
        _permissions   = permissions;
        _distributions = distributions;
        _divisions     = divisions;
        _audit         = audit;
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

        int? scopeDivision = scope.DivisionId;

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
                            d.Id, d.IssueRef, d.DivisionId.ToString(),
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

    // ── AllocateAsync ──────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<DistributionCreatedDto>>> AllocateAsync(
        User requester,
        string stockNo,
        CreateItemDistributionDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a distribution without CanAccessInventory.",
                requester.Id);
            return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.Forbidden(
                "You do not have permission to access Inventory.");
        }

        if (dto.Splits is null || dto.Splits.Count == 0)
            return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.BadRequest(
                "At least one distribution split is required.");

        foreach (DistributionSplitDto split in dto.Splits)
        {
            if (split.QtyIssued <= 0)
                return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.BadRequest(
                    "QtyIssued must be greater than zero for every split.");
            if (string.IsNullOrWhiteSpace(split.IssuedBy))
                return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.BadRequest(
                    "IssuedBy is required for every split.");
            if (string.IsNullOrWhiteSpace(split.Division))
                return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.BadRequest(
                    "Division is required for every split.");
        }

        // Resolve division names to configurable division ids once (v1.2 — RAL-97).
        IReadOnlyList<Division> allDivisions = await _divisions.GetAllAsync(cancellationToken);
        Dictionary<string, Division> divisionByName = allDivisions
            .Where(d => d.IsActive)
            .GroupBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (DistributionSplitDto split in dto.Splits)
            if (!divisionByName.ContainsKey(split.Division))
                return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.BadRequest(
                    $"Division '{split.Division}' was not found. Configure it in Config → Divisions first.");

        // Division scope determines which delivery batches (source stock) are visible —
        // same rule as GetItemSummaryAsync. Office users (no division) see nothing.
        DivisionScope scope = DivisionScope.Resolve(requester);
        if (scope.SeeNothing)
            return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.NotFound(
                $"No activity found for StockNo '{stockNo}'.");

        IReadOnlyList<DeliveryItemBreakdownRow> batches =
            await _deliveries.GetDeliveryItemBreakdownsByStockNoAsync(stockNo, scope.DivisionId, cancellationToken);

        if (batches.Count == 0)
            return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.NotFound(
                $"No delivery activity found for StockNo '{stockNo}'.");

        // FIFO pool — oldest DeliveryDate first, tie-broken by DeliveryItemId for determinism.
        List<(DeliveryItemBreakdownRow Batch, decimal Remaining)> pool = batches
            .Select(b => (Batch: b, Remaining: b.QtyDelivered - b.Distributions.Sum(d => d.QtyIssued)))
            .Where(x => x.Remaining > 0)
            .OrderBy(x => x.Batch.DeliveryDate)
            .ThenBy(x => x.Batch.DeliveryItemId)
            .ToList();

        decimal totalAvailable = pool.Sum(x => x.Remaining);
        decimal totalRequested = dto.Splits.Sum(s => s.QtyIssued);

        if (totalRequested > totalAvailable)
            return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.BadRequest(
                $"Requested quantity ({totalRequested}) exceeds available stock ({totalAvailable}) for StockNo '{stockNo}'.");

        ItemMaster? master = await _items.GetByStockNoAsync(stockNo, cancellationToken);

        DateTime manilaNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaZone);
        string   suffix    = new(Enumerable.Range(0, 5)
                                    .Select(_ => RefChars[Random.Shared.Next(RefChars.Length)])
                                    .ToArray());
        int issueSeq = 1; // shared suffix, running sequence across the whole allocation

        List<Distribution> created = new();
        List<DistributionCreatedDto> createdDtos = new();

        foreach (DistributionSplitDto split in dto.Splits)
        {
            decimal need     = split.QtyIssued;
            Division division = divisionByName[split.Division];

            for (int i = 0; i < pool.Count && need > 0; i++)
            {
                (DeliveryItemBreakdownRow batch, decimal remaining) = pool[i];
                if (remaining <= 0) continue;

                decimal take     = Math.Min(need, remaining);
                string  issueRef = $"ISS-{manilaNow:yyyyMMdd}-{suffix}-{issueSeq++}";

                Distribution dist = new()
                {
                    Id             = Guid.NewGuid(),
                    IssueRef       = issueRef,
                    DeliveryItemId = batch.DeliveryItemId,
                    DivisionId     = division.Id,
                    QtyIssued      = take,
                    DateIssued     = split.DateIssued,
                    IssuedBy       = split.IssuedBy.Trim(),
                    Remarks        = split.Remarks?.Trim(),
                };

                created.Add(dist);
                createdDtos.Add(new DistributionCreatedDto(
                    Id:             dist.Id,
                    IssueRef:       issueRef,
                    DeliveryItemId: batch.DeliveryItemId,
                    DeliveryRef:    batch.DeliveryRef,
                    PRNo:           batch.PRNo,
                    StockNo:        stockNo,
                    Description:    master?.Description ?? stockNo,
                    Division:       division.Name,
                    QtyIssued:      take,
                    DateIssued:     split.DateIssued,
                    IssuedBy:       dist.IssuedBy,
                    Remarks:        dist.Remarks));

                pool[i] = (batch, remaining - take);
                need   -= take;
            }
        }

        foreach (Distribution dist in created)
            await _distributions.AddAsync(dist, cancellationToken);
        await _distributions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Distribution allocated. StockNo: {StockNo}, Splits: {SplitCount}, Records: {RecordCount}, TotalQty: {TotalQty}, UserId: {UserId}",
            stockNo, dto.Splits.Count, created.Count, totalRequested, requester.Id);

        // One entry per created row — each is its own auditable stock movement (division,
        // qty, batch), not a line item subordinate to a bigger aggregate.
        foreach (Distribution dist in created)
            await _audit.LogAsync("Distributions", dist.Id, AuditAction.Create,
                oldValues: null,
                newValues: new
                {
                    StockNo = stockNo, dist.DeliveryItemId, DivisionId = dist.DivisionId,
                    dist.QtyIssued, dist.DateIssued, dist.IssuedBy, dist.IssueRef,
                },
                cancellationToken);

        return ServiceResult<IReadOnlyList<DistributionCreatedDto>>.Ok(createdDtos);
    }
}
