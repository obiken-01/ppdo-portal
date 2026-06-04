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
    private readonly ILogger<ItemService>     _logger;

    public ItemService(
        IItemMasterRepository items,
        IPermissionService permissions,
        ILogger<ItemService> logger)
    {
        _items       = items;
        _permissions = permissions;
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

        if (dto.StockNo is not null)     item.StockNo     = dto.StockNo.Trim();
        if (dto.Description is not null) item.Description = dto.Description.Trim();
        if (dto.Unit is not null)        item.Unit        = dto.Unit.Trim();
        if (dto.UnitCost is not null)    item.UnitCost    = dto.UnitCost.Value;
        if (dto.Category is not null)    item.Category    = string.IsNullOrWhiteSpace(dto.Category) ? null : dto.Category.Trim();
        if (dto.ItemType is not null)    item.ItemType    = string.IsNullOrWhiteSpace(dto.ItemType) ? null : dto.ItemType.Trim();
        if (dto.ReorderQty is not null)  item.ReorderQty  = dto.ReorderQty.Value;
        if (dto.Remarks is not null)     item.Remarks     = string.IsNullOrWhiteSpace(dto.Remarks) ? null : dto.Remarks.Trim();

        // Always apply IsNewItem — setting it to false clears the "★ NEW - review" flag.
        item.IsNewItem = dto.IsNewItem;

        await _items.UpdateAsync(item, cancellationToken);
        await _items.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Item updated. StockNo: {StockNo}, Description: {Description}, IsNewItem: {IsNewItem}, UserId: {UserId}",
            item.StockNo, item.Description, item.IsNewItem, requester.Id);

        return ServiceResult<ItemMasterDto>.Ok(MapToDto(item));
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static ItemMasterDto MapToDto(ItemMaster i) => new(
        i.Id, i.StockNo, i.Description, i.Category,
        i.Unit, i.UnitCost, i.ItemType, i.ReorderQty,
        i.Remarks, i.IsNewItem, i.CreatedAt, i.UpdatedAt);
}
