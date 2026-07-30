using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPurchaseRequestRepository"/>.
/// Provides PR-specific queries on top of the generic <see cref="Repository{T}"/> base.
///
/// Include depth never exceeds 2 levels per the project rules in CLAUDE.md.
/// All queries are async.
/// </summary>
public sealed class PurchaseRequestRepository
    : Repository<PurchaseRequest>, IPurchaseRequestRepository
{
    public PurchaseRequestRepository(AppDbContext context) : base(context) { }

    /// <summary>
    /// Overrides the generic base to eager-load <see cref="PurchaseRequest.Division"/> —
    /// callers (PurchaseRequestService.MapToSummary) read pr.Division?.Name, which is null
    /// without this. Depth 1.
    /// </summary>
    /// <inheritdoc />
    public override async Task<IReadOnlyList<PurchaseRequest>> GetAllAsync(
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Include(pr => pr.Division)       // depth 1
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetByPRNoAsync(
        string prNo,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .FirstOrDefaultAsync(pr => pr.PRNo == prNo, cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetWithItemsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Include(pr => pr.Items)          // depth 1
            .Include(pr => pr.Division)       // depth 1 — sibling include, not nested
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<PurchaseRequest?> GetWithItemsAndDeliveriesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Include(pr => pr.Items)          // depth 1 — sibling includes, not nested
            .Include(pr => pr.Deliveries)     // depth 1
            .Include(pr => pr.Division)       // depth 1
            .FirstOrDefaultAsync(pr => pr.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PurchaseRequest>> GetByDivisionAsync(
        int divisionId,
        CancellationToken cancellationToken = default)
        => await _context.PurchaseRequests
            .Where(pr => pr.DivisionId == divisionId)
            .Include(pr => pr.Division)       // depth 1 — pr.Division?.Name is read downstream
            .OrderByDescending(pr => pr.PRDate)
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Computes MAX over the last '-'-delimited segment of PRNo entirely in SQL — no rows
    /// are pulled into memory. TRY_CAST returns NULL (not an error) for any PRNo whose last
    /// segment isn't a plain integer, and the WHERE clause requires at least 6 dashes (7
    /// segments, matching the 101-1041-GF-YYYY-MM-DD-XXX format) before attempting the cast
    /// at all — together these reproduce the old in-memory ParseSequence's tolerance for
    /// malformed/legacy PRNos (skipped, never fatal) without ever loading purchase_requests.
    /// </summary>
    /// <inheritdoc />
    public async Task<int?> GetMaxPrSequenceAsync(CancellationToken cancellationToken = default)
    {
        List<int?> rows = await _context.Database
            .SqlQueryRaw<int?>(
                """
                SELECT MAX(TRY_CAST(RIGHT(PRNo, CHARINDEX('-', REVERSE(PRNo)) - 1) AS INT)) AS Value
                FROM PurchaseRequests
                WHERE LEN(PRNo) - LEN(REPLACE(PRNo, '-', '')) >= 6
                """)
            .ToListAsync(cancellationToken);

        return rows.Count > 0 ? rows[0] : null;
    }

    /// <inheritdoc />
    public async Task<PurchaseRequestStatsAggregate> GetStatsAggregateAsync(
        int? divisionId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<PurchaseRequest> scoped = divisionId.HasValue
            ? _context.PurchaseRequests.Where(pr => pr.DivisionId == divisionId.Value)
            : _context.PurchaseRequests;

        // Sequential aggregate queries on the shared DbContext — never Task.WhenAll here
        // (DbContext is not thread-safe; see CLAUDE.md's GetStatsAsync prod-500 note).
        int total = await scoped.CountAsync(cancellationToken);
        int open = await scoped.CountAsync(pr => pr.Status == PRStatus.Open, cancellationToken);
        int partiallyDelivered = await scoped.CountAsync(
            pr => pr.Status == PRStatus.PartiallyDelivered, cancellationToken);
        int fullyDeliveredOrCompleted = await scoped.CountAsync(
            pr => pr.Status == PRStatus.FullyDelivered || pr.Status == PRStatus.Completed,
            cancellationToken);
        decimal totalAmount =
            await scoped.SumAsync(pr => (decimal?)pr.TotalAmount, cancellationToken) ?? 0m;

        return new PurchaseRequestStatsAggregate(
            total, open, partiallyDelivered, fullyDeliveredOrCompleted, totalAmount);
    }

    /// <inheritdoc />
    public async Task<PRSearchResult> SearchAsync(
        PRSearchCriteria criteria,
        int? divisionId,
        CancellationToken cancellationToken = default)
    {
        IQueryable<PurchaseRequest> query = _context.PurchaseRequests
            .Include(pr => pr.Division);      // depth 1 — pr.Division?.Name is read downstream

        if (divisionId.HasValue)
            query = query.Where(pr => pr.DivisionId == divisionId.Value);

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            string s = criteria.Search.Trim();
            query = query.Where(pr =>
                pr.PRNo.Contains(s)
                || (pr.Division != null && pr.Division.Name.Contains(s))
                || pr.RequestedBy.Contains(s)
                || pr.Fund.Contains(s));
        }

        if (criteria.DateFrom.HasValue)
            query = query.Where(pr => pr.PRDate >= criteria.DateFrom.Value);
        if (criteria.DateTo.HasValue)
            query = query.Where(pr => pr.PRDate <= criteria.DateTo.Value);

        if (criteria.Statuses is { Count: > 0 })
            query = query.Where(pr => criteria.Statuses.Contains(pr.Status));

        if (!string.IsNullOrWhiteSpace(criteria.Division))
            query = query.Where(pr => pr.Division != null && pr.Division.Name == criteria.Division);

        if (!string.IsNullOrWhiteSpace(criteria.RequestedBy))
            query = query.Where(pr => pr.RequestedBy.Contains(criteria.RequestedBy));
        if (!string.IsNullOrWhiteSpace(criteria.Fund))
            query = query.Where(pr => pr.Fund.Contains(criteria.Fund));
        if (!string.IsNullOrWhiteSpace(criteria.AIPCode))
            query = query.Where(pr => pr.AIPCode != null && pr.AIPCode.Contains(criteria.AIPCode));
        if (!string.IsNullOrWhiteSpace(criteria.AccountNo))
            query = query.Where(pr => pr.AccountNo != null && pr.AccountNo.Contains(criteria.AccountNo));
        if (!string.IsNullOrWhiteSpace(criteria.AccountTitle))
            query = query.Where(pr => pr.AccountTitle != null && pr.AccountTitle.Contains(criteria.AccountTitle));
        if (!string.IsNullOrWhiteSpace(criteria.Program))
            query = query.Where(pr => pr.Program != null && pr.Program.Contains(criteria.Program));
        if (!string.IsNullOrWhiteSpace(criteria.Project))
            query = query.Where(pr => pr.Project != null && pr.Project.Contains(criteria.Project));
        if (!string.IsNullOrWhiteSpace(criteria.Activity))
            query = query.Where(pr => pr.Activity != null && pr.Activity.Contains(criteria.Activity));

        // Status counts within the filtered set (sequential — see GetStatsAggregateAsync note above).
        int countOpen = await query.CountAsync(pr => pr.Status == PRStatus.Open, cancellationToken);
        int countPartiallyDelivered = await query.CountAsync(
            pr => pr.Status == PRStatus.PartiallyDelivered, cancellationToken);
        int countFullyDelivered = await query.CountAsync(
            pr => pr.Status == PRStatus.FullyDelivered, cancellationToken);
        int countCompleted = await query.CountAsync(pr => pr.Status == PRStatus.Completed, cancellationToken);
        int totalCount = countOpen + countPartiallyDelivered + countFullyDelivered + countCompleted;

        query = ApplySort(query, criteria.SortBy, criteria.SortDescending);

        List<PurchaseRequest> items = await query
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .ToListAsync(cancellationToken);

        return new PRSearchResult(
            items, totalCount,
            new PRStatusCounts(countOpen, countPartiallyDelivered, countFullyDelivered, countCompleted));
    }

    /// <summary>Applies the PR List page's 6 sortable columns; defaults to PRDate descending
    /// (the page's own default) when SortBy is missing/unrecognized.</summary>
    private static IQueryable<PurchaseRequest> ApplySort(
        IQueryable<PurchaseRequest> query, string? sortBy, bool descending)
    {
        Expression<Func<PurchaseRequest, object>> keySelector = sortBy?.ToLowerInvariant() switch
        {
            "prno"         => pr => pr.PRNo,
            "division"     => pr => pr.Division != null ? pr.Division.Name : "",
            "requestedby"  => pr => pr.RequestedBy,
            "fund"         => pr => pr.Fund,
            "totalamount"  => pr => pr.TotalAmount,
            "prdate"       => pr => pr.PRDate,
            _              => pr => pr.PRDate,
        };

        return descending ? query.OrderByDescending(keySelector) : query.OrderBy(keySelector);
    }
}
