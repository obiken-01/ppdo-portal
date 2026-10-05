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
    private readonly IAuditService           _audit;
    private readonly ILogger<StockBalanceService> _logger;

    public StockBalanceService(
        IStockBalanceRepository stockBalances,
        IInventoryRepository inventory,
        IItemMasterRepository items,
        IUserRepository users,
        IPermissionService permissions,
        IExcelService excel,
        IAuditService audit,
        ILogger<StockBalanceService> logger)
    {
        _stockBalances = stockBalances;
        _inventory     = inventory;
        _items         = items;
        _users         = users;
        _permissions   = permissions;
        _excel         = excel;
        _audit         = audit;
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

    // ── GetSystemOnHandAsync ──────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<SystemOnHandDto>> GetSystemOnHandAsync(
        User requester,
        string stockNo,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<SystemOnHandDto>.Forbidden(
                "You do not have permission to view warehouse stock input.");

        if (string.IsNullOrWhiteSpace(stockNo))
            return ServiceResult<SystemOnHandDto>.BadRequest("stockNo is required.");

        string trimmed = stockNo.Trim();
        decimal onHand = await ComputeSystemOnHandAsync(trimmed, excludeExistingVariance: 0m, cancellationToken);

        return ServiceResult<SystemOnHandDto>.Ok(new SystemOnHandDto(trimmed, onHand));
    }

    // ── GetImportTemplateAsync ────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<byte[]>> GetImportTemplateAsync(
        User requester,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<byte[]>.Forbidden(
                "You do not have permission to manage warehouse stock input.");

        byte[] bytes = _excel.GenerateStockBalanceImportTemplate();
        return ServiceResult<byte[]>.Ok(bytes);
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

        // (StockNo, EffectiveDate) is unique at the DB level — without this check, recording
        // a second count for a date that already has one throws an unhandled DbUpdateException
        // instead of a friendly message. The bulk-import path avoids this because it upserts;
        // the single-entry form here has no such semantics, so it must reject explicitly.
        StockBalance? duplicate = await _stockBalances.FindByStockNoAndEffectiveDateAsync(
            stockNo, dto.EffectiveDate, cancellationToken);
        if (duplicate is not null)
            return ServiceResult<StockBalanceDto>.Conflict(
                $"A count for {stockNo} on {dto.EffectiveDate:yyyy-MM-dd} was already recorded. Edit that entry instead of creating a new one.");

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

        await _audit.LogAsync("stock_balances", entry.Id, AuditAction.Create,
            oldValues: null,
            newValues: AuditSnapshot(entry),
            cancellationToken);

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
        decimal newVariance = countedQty - systemOnHand;

        Dictionary<string, object?> oldValues = new();
        Dictionary<string, object?> newValues = new();
        void Track(string field, object? oldVal, object? newVal)
        {
            if (Equals(oldVal, newVal)) return;
            oldValues[field] = oldVal;
            newValues[field] = newVal;
        }

        Track("CountedQty", entry.CountedQty, countedQty);
        Track("EffectiveDate", entry.EffectiveDate, effectiveDate);
        Track("VarianceQty", entry.VarianceQty, newVariance);
        if (dto.Reason is not null)
        {
            string? v = string.IsNullOrWhiteSpace(dto.Reason) ? null : dto.Reason.Trim();
            Track("Reason", entry.Reason, v);
            entry.Reason = v;
        }

        entry.CountedQty          = countedQty;
        entry.EffectiveDate       = effectiveDate;
        entry.SystemOnHandAtEntry = systemOnHand;
        entry.VarianceQty         = newVariance;

        await _stockBalances.UpdateAsync(entry, cancellationToken);
        await _stockBalances.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stock balance entry updated. StockNo: {StockNo}, CountedQty: {CountedQty}, VarianceQty: {VarianceQty}, UserId: {UserId}",
            entry.StockNo, entry.CountedQty, entry.VarianceQty, requester.Id);

        if (oldValues.Count > 0)
            await _audit.LogAsync("stock_balances", entry.Id, AuditAction.Update,
                oldValues, newValues, cancellationToken);

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
        object deletedSnapshot = AuditSnapshot(entry);

        await _stockBalances.DeleteAsync(entry, cancellationToken);
        await _stockBalances.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Stock balance entry deleted. StockNo: {StockNo}, UserId: {UserId}",
            entry.StockNo, requester.Id);

        await _audit.LogAsync("stock_balances", entry.Id, AuditAction.Delete,
            oldValues: deletedSnapshot,
            newValues: null,
            cancellationToken);

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

        // The whole file commits or none of it does — a mid-loop failure (bad row, or an
        // infrastructure blip against a cold-resuming DB) must not leave rows 1..n-1 live
        // while the caller sees a failure and assumes nothing saved (RAL-207).
        //
        // Set-based (PPDO-189): the loop used to run five queries and a SaveChanges per row, all
        // inside this transaction. Now everything it needs — the item masters, the existing
        // (StockNo, EffectiveDate) balances and each StockNo's on-hand inputs — is loaded up front
        // in a handful of grouped queries, the rows are applied in memory, and the result is saved
        // once. The "multiple rows for one StockNo stack correctly" guarantee that the per-row
        // flush used to give is kept by updating the in-memory running totals as each row is applied.
        //
        // ExecuteInTransactionAsync may retry this delegate on a transient fault — reset the
        // accumulators at the top so a retried run doesn't double up on a partial prior attempt,
        // and drop whatever the failed attempt left in the change tracker (see ResetChangeTracking).
        int attempt = 0;
        try
        {
            await _stockBalances.ExecuteInTransactionAsync(async () =>
            {
                if (attempt++ > 0) _stockBalances.ResetChangeTracking();
                inserted = 0;
                updated = 0;
                saved.Clear();

                // Rows with a blank StockNo are left out of the loads; the loop rejects them with
                // their own row-level message when it reaches them.
                List<string> stockNos = dto.Rows
                    .Select(r => r.StockNo?.Trim())
                    .Where(sn => !string.IsNullOrEmpty(sn))
                    .Select(sn => sn!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                List<DateOnly> dates = dto.Rows.Select(r => r.EffectiveDate).Distinct().ToList();

                // Sequential awaits — never Task.WhenAll over the shared DbContext (CLAUDE.md).
                Dictionary<string, ItemMaster> itemsByStockNo =
                    (await _items.GetByStockNosAsync(stockNos, cancellationToken))
                        .GroupBy(i => i.StockNo, StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

                Dictionary<string, StockBalance> balancesByKey =
                    (await _stockBalances.GetByStockNosAndDatesAsync(stockNos, dates, cancellationToken))
                        .GroupBy(b => BalanceKey(b.StockNo, b.EffectiveDate))
                        .ToDictionary(g => g.Key, g => g.First());

                IReadOnlyDictionary<string, ItemStockLevel> levels =
                    await _inventory.GetItemStockLevelsByStockNosAsync(stockNos, cancellationToken);

                // Running SUM(VarianceQty) per StockNo — what ComputeSystemOnHandAsync used to
                // re-query after every row. Seeded from the database, then kept current below.
                Dictionary<string, decimal> runningVariance =
                    (await _stockBalances.GetTotalVarianceByStockNosAsync(stockNos, cancellationToken))
                        .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

                // Audit entries wait until the rows are saved: the audit service saves on every call.
                List<(Guid Id, string Action, object? Old, object? New)> auditEntries = new();

                foreach (CreateStockBalanceDto row in dto.Rows)
                {
                    string? validationError = Validate(row.StockNo, row.CountedQty, row.EffectiveDate);
                    if (validationError is not null)
                        throw new StockBalanceImportRowException(
                            $"StockNo '{row.StockNo}': {validationError}");

                    string stockNo = row.StockNo.Trim();

                    // An item staged by an earlier row of this file counts as cataloged for later
                    // rows — otherwise a repeated unknown StockNo would insert its master twice.
                    bool itemAutoCreated = false;
                    if (!itemsByStockNo.ContainsKey(stockNo))
                    {
                        (ItemMaster? newItem, string? itemError) = await StageNewItemMasterAsync(
                            stockNo, row.Description, row.Unit, row.UnitCost, row.ItemType, cancellationToken);
                        if (itemError is not null)
                            throw new StockBalanceImportRowException($"StockNo '{stockNo}': {itemError}");

                        itemsByStockNo[stockNo] = newItem!;
                        itemAutoCreated = true;
                    }

                    // Upsert by StockNo + EffectiveDate — re-uploading the same pair overwrites it
                    // (including a pair an earlier row of this same file just inserted).
                    string key = BalanceKey(stockNo, row.EffectiveDate);
                    balancesByKey.TryGetValue(key, out StockBalance? existing);

                    decimal movementOnHand = levels.TryGetValue(stockNo, out ItemStockLevel? level)
                        ? level.QtyDelivered - level.QtyDistributed
                        : 0m;
                    decimal excludeVariance = existing?.VarianceQty ?? 0m;
                    decimal systemOnHand = movementOnHand
                        + (runningVariance.GetValueOrDefault(stockNo, 0m) - excludeVariance);

                    if (existing is not null)
                    {
                        object oldSnapshot = AuditSnapshot(existing);
                        decimal oldVariance = existing.VarianceQty;

                        existing.CountedQty          = row.CountedQty;
                        existing.SystemOnHandAtEntry = systemOnHand;
                        existing.VarianceQty         = row.CountedQty - systemOnHand;
                        existing.Reason              = string.IsNullOrWhiteSpace(row.Reason) ? null : row.Reason.Trim();

                        await _stockBalances.UpdateAsync(existing, cancellationToken);
                        runningVariance[stockNo] = runningVariance.GetValueOrDefault(stockNo, 0m)
                            + (existing.VarianceQty - oldVariance);
                        saved.Add((existing, itemAutoCreated));
                        updated++;

                        // Per-row, not one summarized entry for the whole file — a bulk overwrite of
                        // computed on-hand values (no approval step) is exactly the kind of change
                        // that needs individual accountability, not a rollup that hides which rows
                        // actually changed.
                        auditEntries.Add((existing.Id, AuditAction.Update, oldSnapshot, AuditSnapshot(existing)));
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
                        balancesByKey[key] = entry;
                        runningVariance[stockNo] = runningVariance.GetValueOrDefault(stockNo, 0m) + entry.VarianceQty;
                        saved.Add((entry, itemAutoCreated));
                        inserted++;

                        auditEntries.Add((entry.Id, AuditAction.Create, null, AuditSnapshot(entry)));
                    }
                }

                // One flush for the whole file: the new item masters (same shared AppDbContext) and
                // every inserted/updated balance.
                await _stockBalances.SaveChangesAsync(cancellationToken);

                foreach ((Guid id, string action, object? oldValues, object? newValues) in auditEntries)
                    await _audit.LogAsync("stock_balances", id, action, oldValues, newValues, cancellationToken);
            }, cancellationToken);
        }
        catch (StockBalanceImportRowException ex)
        {
            return ServiceResult<StockBalanceImportResultDto>.BadRequest(ex.Message);
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

        (ItemMaster? created, string? error) = await StageNewItemMasterAsync(
            stockNo, description, unit, unitCost, itemType, cancellationToken);
        return (created is not null, error);
    }

    /// <summary>
    /// The "StockNo is not yet in Items Master" half of <see cref="EnsureItemMasterAsync"/>:
    /// validates Description/Unit and stages a new ItemMaster (IsNewItem = true) on the shared
    /// context. Returns the staged row, or an error message. Split out so the bulk import, which
    /// has already loaded the catalogue, can skip the per-row lookup.
    /// </summary>
    private async Task<(ItemMaster? Item, string? Error)> StageNewItemMasterAsync(
        string stockNo,
        string? description,
        string? unit,
        decimal? unitCost,
        string? itemType,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(description))
            return (null, "Description is required — StockNo is not yet in Items Master.");
        if (string.IsNullOrWhiteSpace(unit))
            return (null, "Unit is required — StockNo is not yet in Items Master.");

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

        return (newMaster, null);
    }

    /// <summary>Case-insensitive (StockNo, EffectiveDate) key — the database collation compares StockNo that way.</summary>
    private static string BalanceKey(string stockNo, DateOnly effectiveDate)
        => $"{stockNo.ToUpperInvariant()}|{effectiveDate:yyyy-MM-dd}";

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

    /// <summary>
    /// Carries a row-level validation/lookup failure out of the
    /// <see cref="IRepository{T}.ExecuteInTransactionAsync"/> delegate in <see cref="CommitImportAsync"/>
    /// so it triggers a rollback (rows already saved in this file are undone) and is then
    /// translated back into a BadRequest — same message the pre-transaction code returned directly.
    /// </summary>
    private sealed class StockBalanceImportRowException(string message) : Exception(message);

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

    private static object AuditSnapshot(StockBalance e) => new
    {
        e.StockNo, e.CountedQty, e.SystemOnHandAtEntry, e.VarianceQty,
        e.EffectiveDate, e.Reason, e.RecordedByUserId,
    };
}
