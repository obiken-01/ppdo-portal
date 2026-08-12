using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Items;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Items Master CRUD — add, update, and look up catalog items.
///
/// Read operations are unrestricted (all authenticated users).
/// Write operations require CanAccessInventory permission (Admin/SuperAdmin always;
/// Staff/Observer via PermissionService resolution).
/// </summary>
public sealed class ItemService : IItemService
{
    private readonly IItemMasterRepository    _items;
    private readonly IPermissionService       _permissions;
    private readonly IAuditService            _audit;
    private readonly ILogger<ItemService>     _logger;

    public ItemService(
        IItemMasterRepository items,
        IPermissionService permissions,
        IAuditService audit,
        ILogger<ItemService> logger)
    {
        _items       = items;
        _permissions = permissions;
        _audit       = audit;
        _logger      = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMasterDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ItemMaster> all = await _items.GetAllAsync(cancellationToken);
        return all.OrderBy(i => i.StockNo).Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<ItemMasterSearchResultDto> SearchAsync(
        ItemMasterSearchFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        ItemMasterSearchCriteria criteria = new(
            Search:         filter.Search,
            StockNo:        filter.StockNo,
            Description:    filter.Description,
            Category:       filter.Category,
            Unit:           filter.Unit,
            ItemType:       filter.ItemType,
            Remarks:        filter.Remarks,
            IsNewOnly:      filter.IsNewOnly,
            SortBy:         filter.SortBy,
            SortDescending: filter.SortDescending,
            Page:           filter.Page,
            PageSize:       filter.PageSize);

        ItemMasterSearchResult result = await _items.SearchAsync(criteria, cancellationToken);

        return new ItemMasterSearchResultDto(
            result.Items.Select(MapToDto).ToList(),
            result.TotalCount,
            result.TotalNewItemCount,
            filter.Page,
            filter.PageSize);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItemMasterDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        ItemMaster? item = await _items.GetByIdAsync(id, cancellationToken);
        return item is null
            ? ServiceResult<ItemMasterDto>.NotFound($"Item {id} not found.")
            : ServiceResult<ItemMasterDto>.Ok(MapToDto(item));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemLookupDto>> LookupAsync(
        string term,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(term))
            return [];

        IReadOnlyList<ItemMaster> results =
            await _items.SearchAsync(term.Trim(), cancellationToken);

        return results.Select(i => new ItemLookupDto(
            i.Id, i.StockNo, i.Description, i.Unit, i.UnitCost)).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItemMasterDto>> CreateAsync(
        User requester,
        CreateItemMasterDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create an item without CanAccessInventory.",
                requester.Id);
            return ServiceResult<ItemMasterDto>.Forbidden(
                "You do not have permission to manage Items Master.");
        }

        if (string.IsNullOrWhiteSpace(dto.StockNo))
            return ServiceResult<ItemMasterDto>.BadRequest("StockNo is required.");

        if (string.IsNullOrWhiteSpace(dto.Description))
            return ServiceResult<ItemMasterDto>.BadRequest("Description is required.");

        // Aligned to Price Index's 300-char convention (PriceIndexItem.Name) — RAL-226.
        if (dto.Description.Length > 300)
            return ServiceResult<ItemMasterDto>.BadRequest(
                $"Description must be 300 characters or fewer (got {dto.Description.Length}).");

        ItemMaster? existing = await _items.GetByStockNoAsync(dto.StockNo.Trim(), cancellationToken);
        if (existing is not null)
            return ServiceResult<ItemMasterDto>.Conflict(
                $"StockNo '{dto.StockNo}' already exists in the catalog.");

        ItemMaster item = new()
        {
            Id          = Guid.NewGuid(),
            StockNo     = dto.StockNo.Trim(),
            Description = dto.Description.Trim(),
            Unit        = dto.Unit.Trim(),
            UnitCost    = dto.UnitCost,
            Category    = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim(),
            ItemType    = string.IsNullOrWhiteSpace(dto.ItemType) ? null : dto.ItemType.Trim(),
            ReorderQty  = dto.ReorderQty,
            Remarks     = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim(),
            IsNewItem   = dto.IsNewItem,
        };

        await _items.AddAsync(item, cancellationToken);
        await _items.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Item created. StockNo: {StockNo}, Description: {Description}, UserId: {UserId}",
            item.StockNo, item.Description, requester.Id);

        await _audit.LogAsync("ItemMasters", item.Id, AuditAction.Create,
            oldValues: null,
            newValues: AuditSnapshot(item),
            cancellationToken);

        return ServiceResult<ItemMasterDto>.Ok(MapToDto(item));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ItemMasterDto>> UpdateAsync(
        User requester,
        Guid id,
        UpdateItemMasterDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to update item {ItemId} without CanAccessInventory.",
                requester.Id, id);
            return ServiceResult<ItemMasterDto>.Forbidden(
                "You do not have permission to manage Items Master.");
        }

        ItemMaster? item = await _items.GetByIdAsync(id, cancellationToken);
        if (item is null)
            return ServiceResult<ItemMasterDto>.NotFound($"Item {id} not found.");

        // Aligned to Price Index's 300-char convention (PriceIndexItem.Name) — RAL-226.
        if (dto.Description is not null && dto.Description.Length > 300)
            return ServiceResult<ItemMasterDto>.BadRequest(
                $"Description must be 300 characters or fewer (got {dto.Description.Length}).");

        Dictionary<string, object?> oldValues = new();
        Dictionary<string, object?> newValues = new();
        void Track(string field, object? oldVal, object? newVal)
        {
            if (Equals(oldVal, newVal)) return;
            oldValues[field] = oldVal;
            newValues[field] = newVal;
        }

        if (dto.StockNo is not null)     { string v = dto.StockNo.Trim(); Track("StockNo", item.StockNo, v); item.StockNo = v; }
        if (dto.Description is not null) { string v = dto.Description.Trim(); Track("Description", item.Description, v); item.Description = v; }
        if (dto.Unit is not null)        { string v = dto.Unit.Trim(); Track("Unit", item.Unit, v); item.Unit = v; }
        if (dto.UnitCost is not null)    { Track("UnitCost", item.UnitCost, dto.UnitCost.Value); item.UnitCost = dto.UnitCost.Value; }
        if (dto.Category is not null)
        {
            string? v = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
            Track("Category", item.Category, v); item.Category = v;
        }
        if (dto.ItemType is not null)
        {
            string? v = string.IsNullOrWhiteSpace(dto.ItemType) ? null : dto.ItemType.Trim();
            Track("ItemType", item.ItemType, v); item.ItemType = v;
        }
        if (dto.ReorderQty is not null)  { Track("ReorderQty", item.ReorderQty, dto.ReorderQty.Value); item.ReorderQty = dto.ReorderQty.Value; }
        if (dto.Remarks is not null)
        {
            string? v = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();
            Track("Remarks", item.Remarks, v); item.Remarks = v;
        }

        // Always apply IsNewItem — setting it to false clears the "★ NEW - review" flag.
        Track("IsNewItem", item.IsNewItem, dto.IsNewItem);
        item.IsNewItem = dto.IsNewItem;

        await _items.UpdateAsync(item, cancellationToken);
        await _items.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Item updated. StockNo: {StockNo}, Description: {Description}, IsNewItem: {IsNewItem}, UserId: {UserId}",
            item.StockNo, item.Description, item.IsNewItem, requester.Id);

        if (oldValues.Count > 0)
            await _audit.LogAsync("ItemMasters", item.Id, AuditAction.Update,
                oldValues, newValues, cancellationToken);

        return ServiceResult<ItemMasterDto>.Ok(MapToDto(item));
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static ItemMasterDto MapToDto(ItemMaster i) => new(
        i.Id, i.StockNo, i.Description, i.Category,
        i.Unit, i.UnitCost, i.ItemType, i.ReorderQty,
        i.Remarks, i.IsNewItem, i.CreatedAt, i.UpdatedAt);

    private static object AuditSnapshot(ItemMaster i) => new
    {
        i.StockNo, i.Description, i.Category, i.Unit,
        i.UnitCost, i.ItemType, i.ReorderQty, i.Remarks, i.IsNewItem,
    };
}
