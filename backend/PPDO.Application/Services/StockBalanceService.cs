using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Inventory;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Warehouse stock input (RAL-193) — a recurring physical-count ledger.
///
/// On-hand formula (see InventoryService for where this feeds in):
///   onHand = SUM(StockBalance.VarianceQty for the StockNo) + QtyDelivered - QtyDistributed
///
/// Each entry's VarianceQty = CountedQty - SystemOnHandAtEntry, computed once at save time
/// as "what the formula above evaluates to right now, excluding this entry's own prior
/// contribution". This is an approximation for edits made out of chronological order (it
/// reflects the *current* system state, not a point-in-time replay) — acceptable here since
/// there is no approval workflow and entries are meant to be corrected by re-editing, not
/// audited as an immutable ledger.
///
/// PPDO-wide: CanAccessInventory holders can create/edit/delete (no approval step, no
/// division scope on the table).
///
/// Unknown StockNo handling mirrors PurchaseRequestService.BuildItemsAsync: recording a
/// count for a StockNo not yet in Items Master auto-creates the catalog entry
/// (IsNewItem = true, pending admin review) from the Description/Unit/UnitCost/ItemType
/// supplied alongside the count. When the StockNo is already known, those fields are
/// ignored — the catalog's own values win, same as the PR flow.
/// </summary>
public sealed class StockBalanceService : IStockBalanceService
{
    private readonly IStockBalanceRepository _stockBalances;
    private readonly IInventoryRepository    _inventory;
    private readonly IItemMasterRepository   _items;
    private readonly IUserRepository         _users;
    private readonly IPermissionService      _permissions;
    private readonly IExcelService           _excel;
    private readonly ILogger<StockBalanceService> _logger;

    public StockBalanceService(
        IStockBalanceRepository stockBalances,
        IInventoryRepository inventory,
        IItemMasterRepository items,
        IUserRepository users,
        IPermissionService permissions,
        IExcelService excel,
        ILogger<StockBalanceService> logger)
    {
        _stockBalances = stockBalances;
        _inventory     = inventory;
        _items         = items;
        _users         = users;
        _permissions   = permissions;
        _excel         = excel;
        _logger        = logger;
    }

    // ── GetHistoryAsync ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<StockBalanceDto>>> GetHistoryAsync(
        User requester,
        string stockNo,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<IReadOnlyList<StockBalanceDto>>.Forbidden(
                "You do not have permission to view warehouse stock input.");

        IReadOnlyList<StockBalance> entries =
            await _stockBalances.GetByStockNoAsync(stockNo, cancellationToken);

        IReadOnlyList<StockBalanceDto> dtos = await MapToDtosAsync(entries, cancellationToken);
        return ServiceResult<IReadOnlyList<StockBalanceDto>>.Ok(dtos);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<StockBalanceDto>> CreateAsync(
        User requester,
        CreateStockBalanceDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a stock balance entry without CanAccessInventory.",
                requester.Id);
            return ServiceResult<StockBalanceDto>.Forbidden(
                "You do not have permission to manage warehouse stock input.");
        }

        string? validationError = Validate(dto.StockNo, dto.CountedQty, dto.EffectiveDate);
        if (validationError is not null)
            return ServiceResult<StockBalanceDto>.BadRequest(validationError);

        string stockNo = dto.StockNo.Trim();

        (bool itemAutoCreated, string? itemError) = await EnsureItemMasterAsync(
            stockNo, dto.Description, dto.Unit, dto.UnitCost, dto.ItemType, cancellationToken);
        if (itemError is not null)
            return ServiceResult<StockBalanceDto>.BadRequest(itemError);

        decimal systemOnHand = await ComputeSystemOnHandAsync(stockNo, excludeExistingVariance: 0m, cancellationToken);

        StockBalance entry = new()
        {
            Id                   = Guid.NewGuid(),
            StockNo              = stockNo,
            CountedQty           = dto.CountedQty,
            SystemOnHandAtEntry  = systemOnHand,
            VarianceQty          = dto.CountedQty - systemOnHand,
            EffectiveDate        = dto.EffectiveDate,
            Reason               = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim(),
            RecordedByUserId     = requester.Id,
        };

        await _stockBalances.AddAsync(entry, cancellationToken);
        // One SaveChanges flushes both this entry and any ItemMaster row EnsureItemMasterAsync
        // just staged — same shared AppDbContext, same pattern as BuildItemsAsync.
        await _stockBalances.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stock balance entry created. StockNo: {StockNo}, CountedQty: {CountedQty}, VarianceQty: {VarianceQty}, UserId: {UserId}",
            entry.StockNo, entry.CountedQty, entry.VarianceQty, requester.Id);

        return ServiceResult<StockBalanceDto>.Ok(await MapToDtoAsync(entry, cancellationToken, itemAutoCreated));
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<StockBalanceDto>> UpdateAsync(
        User requester,
        Guid id,
        UpdateStockBalanceDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to update stock balance entry {EntryId} without CanAccessInventory.",
                requester.Id, id);
            return ServiceResult<StockBalanceDto>.Forbidden(
                "You do not have permission to manage warehouse stock input.");
        }

        StockBalance? entry = await _stockBalances.GetByIdAsync(id, cancellationToken);
        if (entry is null)
            return ServiceResult<StockBalanceDto>.NotFound($"Stock balance entry {id} not found.");

        decimal countedQty    = dto.CountedQty ?? entry.CountedQty;
        DateOnly effectiveDate = dto.EffectiveDate ?? entry.EffectiveDate;

        string? validationError = Validate(entry.StockNo, countedQty, effectiveDate);
        if (validationError is not null)
            return ServiceResult<StockBalanceDto>.BadRequest(validationError);

        decimal systemOnHand = await ComputeSystemOnHandAsync(
            entry.StockNo, excludeExistingVariance: entry.VarianceQty, cancellationToken);

        entry.CountedQty          = countedQty;
        entry.EffectiveDate       = effectiveDate;
        entry.SystemOnHandAtEntry = systemOnHand;
        entry.VarianceQty         = countedQty - systemOnHand;
        if (dto.Reason is not null)
            entry.Reason = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();

        await _stockBalances.UpdateAsync(entry, cancellationToken);
        await _stockBalances.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stock balance entry updated. StockNo: {StockNo}, CountedQty: {CountedQty}, VarianceQty: {VarianceQty}, UserId: {UserId}",
            entry.StockNo, entry.CountedQty, entry.VarianceQty, requester.Id);

        return ServiceResult<StockBalanceDto>.Ok(await MapToDtoAsync(entry, cancellationToken));
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<StockBalanceDto>> DeleteAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to delete stock balance entry {EntryId} without CanAccessInventory.",
                requester.Id, id);
            return ServiceResult<StockBalanceDto>.Forbidden(
                "You do not have permission to manage warehouse stock input.");
        }

        StockBalance? entry = await _stockBalances.GetByIdAsync(id, cancellationToken);
        if (entry is null)
            return ServiceResult<StockBalanceDto>.NotFound($"Stock balance entry {id} not found.");

        StockBalanceDto dto = await MapToDtoAsync(entry, cancellationToken);

        await _stockBalances.DeleteAsync(entry, cancellationToken);
        await _stockBalances.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stock balance entry deleted. StockNo: {StockNo}, UserId: {UserId}",
            entry.StockNo, requester.Id);

        return ServiceResult<StockBalanceDto>.Ok(dto);
    }

    // ── PreviewImportAsync ────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<StockBalanceImportPreviewDto>> PreviewImportAsync(
        User requester,
        Stream fileStream,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<StockBalanceImportPreviewDto>.Forbidden(
                "You do not have permission to manage warehouse stock input.");

        IReadOnlyList<StockBalanceImportRow> rows;
        try
        {
            rows = _excel.ParseStockBalanceImport(fileStream);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse stock balance import workbook. UserId: {UserId}", requester.Id);
            return ServiceResult<StockBalanceImportPreviewDto>.BadRequest(
                "The uploaded file could not be read. Make sure it is a .xlsx file matching the expected columns.");
        }

        List<StockBalanceImportRowDto> dtoRows = rows.Select(r => new StockBalanceImportRowDto(
            r.RowNumber, r.StockNo, r.CountedQty, r.EffectiveDate, r.Reason,
            r.Description, r.Unit, r.UnitCost, r.ItemType, r.Error)).ToList();

        return ServiceResult<StockBalanceImportPreviewDto>.Ok(new StockBalanceImportPreviewDto(dtoRows));
    }

    // ── CommitImportAsync ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<StockBalanceImportResultDto>> CommitImportAsync(
        User requester,
        CommitStockBalanceImportDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<StockBalanceImportResultDto>.Forbidden(
                "You do not have permission to manage warehouse stock input.");

        int inserted = 0, updated = 0;
        List<(StockBalance Entry, bool ItemAutoCreated)> saved = new();

        foreach (CreateStockBalanceDto row in dto.Rows)
        {
            string? validationError = Validate(row.StockNo, row.CountedQty, row.EffectiveDate);
            if (validationError is not null)
                return ServiceResult<StockBalanceImportResultDto>.BadRequest(
                    $"StockNo '{row.StockNo}': {validationError}");

            string stockNo = row.StockNo.Trim();

            (bool itemAutoCreated, string? itemError) = await EnsureItemMasterAsync(
                stockNo, row.Description, row.Unit, row.UnitCost, row.ItemType, cancellationToken);
            if (itemError is not null)
                return ServiceResult<StockBalanceImportResultDto>.BadRequest($"StockNo '{stockNo}': {itemError}");

            // Upsert by StockNo + EffectiveDate — re-uploading the same pair overwrites it.
            StockBalance? existing = await _stockBalances.FindByStockNoAndEffectiveDateAsync(
                stockNo, row.EffectiveDate, cancellationToken);

            decimal excludeVariance = existing?.VarianceQty ?? 0m;
            decimal systemOnHand = await ComputeSystemOnHandAsync(stockNo, excludeVariance, cancellationToken);

            if (existing is not null)
            {
                existing.CountedQty          = row.CountedQty;
                existing.SystemOnHandAtEntry = systemOnHand;
                existing.VarianceQty         = row.CountedQty - systemOnHand;
                existing.Reason              = string.IsNullOrWhiteSpace(row.Reason) ? null : row.Reason.Trim();

                await _stockBalances.UpdateAsync(existing, cancellationToken);
                saved.Add((existing, itemAutoCreated));
                updated++;
            }
            else
            {
                StockBalance entry = new()
                {
                    Id                  = Guid.NewGuid(),
                    StockNo             = stockNo,
                    CountedQty          = row.CountedQty,
                    SystemOnHandAtEntry = systemOnHand,
                    VarianceQty         = row.CountedQty - systemOnHand,
                    EffectiveDate       = row.EffectiveDate,
                    Reason              = string.IsNullOrWhiteSpace(row.Reason) ? null : row.Reason.Trim(),
                    RecordedByUserId    = requester.Id,
                };
                await _stockBalances.AddAsync(entry, cancellationToken);
                saved.Add((entry, itemAutoCreated));
                inserted++;
            }

            // Persist each row before computing the next one's system-on-hand snapshot, so
            // multiple rows for the same StockNo in one file stack correctly.
            await _stockBalances.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Stock balance bulk import committed. Inserted: {Inserted}, Updated: {Updated}, UserId: {UserId}",
            inserted, updated, requester.Id);

        IReadOnlyDictionary<Guid, string> names = await _users.GetNamesByIdsAsync(
            saved.Select(s => s.Entry.RecordedByUserId).Distinct().ToList(), cancellationToken);
        IReadOnlyList<StockBalanceDto> savedDtos =
            saved.Select(s => MapToDto(s.Entry, names, s.ItemAutoCreated)).ToList();

        return ServiceResult<StockBalanceImportResultDto>.Ok(
            new StockBalanceImportResultDto(inserted, updated, savedDtos));
    }

    // ── Shared on-hand computation ────────────────────────────────────────────

    /// <summary>
    /// Computes what the on-hand formula evaluates to for a StockNo right now, excluding
    /// one entry's own prior variance contribution (0 for a brand-new entry; the entry's
    /// current VarianceQty when recomputing an edit/upsert of an existing entry).
    /// </summary>
    private async Task<decimal> ComputeSystemOnHandAsync(
        string stockNo, decimal excludeExistingVariance, CancellationToken cancellationToken)
    {
        ItemStockLevel level = await _inventory.GetItemStockLevelAsync(stockNo, cancellationToken);
        decimal movementOnHand = level.QtyDelivered - level.QtyDistributed;

        IReadOnlyDictionary<string, decimal> varianceMap =
            await _stockBalances.GetTotalVarianceByStockNosAsync([stockNo], cancellationToken);
        decimal otherEntriesVariance = varianceMap.GetValueOrDefault(stockNo, 0m) - excludeExistingVariance;

        return movementOnHand + otherEntriesVariance;
    }

    // ── Unknown-StockNo auto-create ───────────────────────────────────────────

    /// <summary>
    /// Ensures a StockNo is cataloged. If it already exists in Items Master, does nothing.
    /// Otherwise validates Description/Unit are present and auto-creates a new ItemMaster row
    /// (IsNewItem = true, pending admin review) — mirrors
    /// PurchaseRequestService.BuildItemsAsync exactly, including ReorderQty defaulting to 0.
    /// Does not call SaveChanges — the caller's own SaveChanges (on the shared AppDbContext)
    /// persists this alongside the StockBalance entry.
    /// </summary>
    private async Task<(bool AutoCreated, string? Error)> EnsureItemMasterAsync(
        string stockNo,
        string? description,
        string? unit,
        decimal? unitCost,
        string? itemType,
        CancellationToken cancellationToken)
    {
        ItemMaster? master = await _items.GetByStockNoAsync(stockNo, cancellationToken);
        if (master is not null)
            return (false, null);

        if (string.IsNullOrWhiteSpace(description))
            return (false, "Description is required — StockNo is not yet in Items Master.");
        if (string.IsNullOrWhiteSpace(unit))
            return (false, "Unit is required — StockNo is not yet in Items Master.");

        ItemMaster newMaster = new()
        {
            Id          = Guid.NewGuid(),
            StockNo     = stockNo,
            Description = description.Trim(),
            Unit        = unit.Trim(),
            UnitCost    = unitCost ?? 0m,
            ItemType    = string.IsNullOrWhiteSpace(itemType) ? null : itemType.Trim(),
            IsNewItem   = true,
            ReorderQty  = 0,
            CreatedAt   = DateTime.UtcNow,
            UpdatedAt   = DateTime.UtcNow,
        };

        await _items.AddAsync(newMaster, cancellationToken);

        _logger.LogWarning(
            "Unknown StockNo auto-created via warehouse stock input. StockNo: {StockNo}, flagged IsNewItem = true.",
            stockNo);

        return (true, null);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private static string? Validate(string? stockNo, decimal countedQty, DateOnly effectiveDate)
    {
        if (string.IsNullOrWhiteSpace(stockNo))
            return "StockNo is required.";
        if (countedQty < 0)
            return "CountedQty must be non-negative.";
        if (effectiveDate > DateOnly.FromDateTime(DateTime.UtcNow))
            return "EffectiveDate cannot be in the future.";
        return null;
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private async Task<StockBalanceDto> MapToDtoAsync(
        StockBalance entry, CancellationToken cancellationToken, bool itemWasAutoCreated = false)
    {
        IReadOnlyDictionary<Guid, string> names = await _users.GetNamesByIdsAsync(
            [entry.RecordedByUserId], cancellationToken);
        return MapToDto(entry, names, itemWasAutoCreated);
    }

    private async Task<IReadOnlyList<StockBalanceDto>> MapToDtosAsync(
        IReadOnlyList<StockBalance> entries, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> names = await _users.GetNamesByIdsAsync(
            entries.Select(e => e.RecordedByUserId).Distinct().ToList(), cancellationToken);
        return entries.Select(e => MapToDto(e, names, itemWasAutoCreated: false)).ToList();
    }

    private static StockBalanceDto MapToDto(
        StockBalance e, IReadOnlyDictionary<Guid, string> names, bool itemWasAutoCreated) => new(
        e.Id, e.StockNo, e.CountedQty, e.SystemOnHandAtEntry, e.VarianceQty, e.EffectiveDate,
        e.Reason, e.RecordedByUserId, names.GetValueOrDefault(e.RecordedByUserId), e.CreatedAt, e.UpdatedAt,
        itemWasAutoCreated);
}
