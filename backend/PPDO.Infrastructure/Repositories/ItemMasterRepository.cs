using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IItemMasterRepository"/>.
/// Provides item-catalog-specific queries on top of the generic <see cref="Repository{T}"/> base.
///
/// All queries are async.
/// </summary>
public sealed class ItemMasterRepository
    : Repository<ItemMaster>, IItemMasterRepository
{
    public ItemMasterRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<ItemMaster?> GetByStockNoAsync(
        string stockNo,
        CancellationToken cancellationToken = default)
        => await _context.ItemMasters
            .FirstOrDefaultAsync(i => i.StockNo == stockNo, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMaster>> GetNewItemsAsync(
        CancellationToken cancellationToken = default)
        => await _context.ItemMasters
            .Where(i => i.IsNewItem)
            .OrderBy(i => i.CreatedAt)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMaster>> SearchAsync(
        string term,
        CancellationToken cancellationToken = default)
        => await _context.ItemMasters
            .Where(i => i.StockNo.Contains(term) || i.Description.Contains(term))
            .OrderBy(i => i.StockNo)
            .Take(20)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ItemMaster>> GetByStockNosAsync(
        IReadOnlyCollection<string> stockNos,
        CancellationToken cancellationToken = default)
    {
        if (stockNos.Count == 0) return [];
        return await _context.ItemMasters
            .Where(i => stockNos.Contains(i.StockNo))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ItemMasterSearchResult> SearchAsync(
        ItemMasterSearchCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        IQueryable<ItemMaster> query = _context.ItemMasters;

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            string s = criteria.Search.Trim();
            query = query.Where(i =>
                i.StockNo.Contains(s)
                || i.Description.Contains(s)
                || (i.Category != null && i.Category.Contains(s))
                || i.Unit.Contains(s)
                || (i.ItemType != null && i.ItemType.Contains(s))
                || (i.Remarks != null && i.Remarks.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(criteria.StockNo))
            query = query.Where(i => i.StockNo.Contains(criteria.StockNo));
        if (!string.IsNullOrWhiteSpace(criteria.Description))
            query = query.Where(i => i.Description.Contains(criteria.Description));
        if (!string.IsNullOrWhiteSpace(criteria.Category))
            query = query.Where(i => i.Category != null && i.Category.Contains(criteria.Category));
        if (!string.IsNullOrWhiteSpace(criteria.Unit))
            query = query.Where(i => i.Unit.Contains(criteria.Unit));
        if (!string.IsNullOrWhiteSpace(criteria.ItemType))
            query = query.Where(i => i.ItemType != null && i.ItemType.Contains(criteria.ItemType));
        if (!string.IsNullOrWhiteSpace(criteria.Remarks))
            query = query.Where(i => i.Remarks != null && i.Remarks.Contains(criteria.Remarks));

        if (criteria.IsNewOnly)
            query = query.Where(i => i.IsNewItem);

        // Sequential queries on the shared DbContext — never Task.WhenAll (see CLAUDE.md).
        int totalCount = await query.CountAsync(cancellationToken);
        int totalNewItemCount = await _context.ItemMasters.CountAsync(i => i.IsNewItem, cancellationToken);

        query = ApplySort(query, criteria.SortBy, criteria.SortDescending);

        List<ItemMaster> items = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new ItemMasterSearchResult(items, totalCount, totalNewItemCount);
    }

    /// <summary>Applies the Items Master page's sortable columns; defaults to StockNo
    /// ascending when SortBy is missing/unrecognized.</summary>
    private static IQueryable<ItemMaster> ApplySort(
        IQueryable<ItemMaster> query, string? sortBy, bool descending)
    {
        Expression<Func<ItemMaster, object>> keySelector = sortBy?.ToLowerInvariant() switch
        {
            "description" => i => i.Description,
            "category"    => i => i.Category ?? "",
            "unit"        => i => i.Unit,
            "unitcost"    => i => i.UnitCost,
            "itemtype"    => i => i.ItemType ?? "",
            "reorderqty"  => i => i.ReorderQty,
            _             => i => i.StockNo,
        };

        return descending
            ? query.OrderByDescending(keySelector)
            : query.OrderBy(keySelector);
    }
}
