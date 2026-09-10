using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IPriceIndexItemRepository"/> (RAL-164).</summary>
public sealed class PriceIndexItemRepository : Repository<PriceIndexItem>, IPriceIndexItemRepository
{
    public PriceIndexItemRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexItem>> GetByIdsAsync(
        IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0) return [];
        return await _context.Set<PriceIndexItem>()
            .Where(p => ids.Contains(p.Id))
            .ToListAsync(ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexItem>> GetFilteredAsync(
        bool? isActive, string? search, CancellationToken ct = default)
        => await Filtered(isActive, search).OrderBy(p => p.Name).ToListAsync(ct);

    /// <inheritdoc />
    public async Task<int> CountAsync(bool? isActive, string? search, CancellationToken ct = default)
        => await Filtered(isActive, search).CountAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexPickerItem>> GetPickerItemsAsync(
        bool? isActive, string? search, CancellationToken ct = default)
        // Projected before ToListAsync so EF emits SELECT of just these five columns — the wide
        // Category/StockCardNo free text is never read off the page, not merely dropped later.
        => await Filtered(isActive, search)
            .OrderBy(p => p.Name)
            .Select(p => new PriceIndexPickerItem(p.Id, p.Name, p.Unit, p.UnitPrice, p.DaysEnabled))
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PriceIndexItem> Items, int TotalCount)> GetPagedAsync(
        bool? isActive, string? search, string? sortColumn, bool sortDescending,
        int page, int pageSize, CancellationToken ct = default)
    {
        IQueryable<PriceIndexItem> query = Filtered(isActive, search);

        int total = await query.CountAsync(ct);

        List<PriceIndexItem> items = await ApplySort(query, sortColumn, sortDescending)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    /// <summary>
    /// The management grid's whitelisted sort columns (RAL-233). Mirrors
    /// <c>PurchaseRequestRepository.ApplySort</c>'s shape: an unrecognized or absent
    /// <paramref name="sortColumn"/> falls back to the default rather than reaching arbitrary
    /// user input into <c>OrderBy</c> — accepting a raw column name here would be a real
    /// injection surface, not just a correctness bug.
    /// </summary>
    private static IQueryable<PriceIndexItem> ApplySort(
        IQueryable<PriceIndexItem> query, string? sortColumn, bool descending)
    {
        Expression<Func<PriceIndexItem, object>> keySelector = sortColumn?.ToLowerInvariant() switch
        {
            "unit"           => p => p.Unit,
            "stockcardno"    => p => p.StockCardNo ?? "",
            "category"       => p => p.Category ?? "",
            "unitprice"      => p => p.UnitPrice,
            "priceupdatedat" => p => p.PriceUpdatedAt,
            "daysenabled"    => p => p.DaysEnabled,
            "isactive"       => p => p.IsActive,
            "name"           => p => p.Name,
            _                => p => p.Name,
        };

        return descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);
    }

    /// <summary>
    /// The shared active/search WHERE, so the list, count and picker reads can never drift apart
    /// — a count that filtered differently from its list would be worse than no count at all.
    /// </summary>
    private IQueryable<PriceIndexItem> Filtered(bool? isActive, string? search)
    {
        IQueryable<PriceIndexItem> query = _context.Set<PriceIndexItem>();

        if (isActive.HasValue)
            query = query.Where(p => p.IsActive == isActive.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            query = query.Where(p =>
                p.Name.Contains(s) ||
                (p.Category != null && p.Category.Contains(s)) ||
                (p.StockCardNo != null && p.StockCardNo.Contains(s)));
        }

        return query;
    }
}
