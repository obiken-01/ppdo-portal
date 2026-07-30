using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// Filter/sort/page parameters for <c>GET /api/purchase-requests/search</c>. All string
/// fields are partial (Contains) matches except Division (exact match against the division
/// name), matching the PR List page's client-side Filters shape exactly.
/// </summary>
public sealed record PRSearchFilterDto(
    int      Page,
    int      PageSize,
    string?  Search,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    IReadOnlyList<PRStatus>? Statuses,
    string?  Division,
    string?  RequestedBy,
    string?  Fund,
    string?  AIPCode,
    string?  AccountNo,
    string?  AccountTitle,
    string?  Program,
    string?  Project,
    string?  Activity,
    string?  SortBy,
    bool     SortDescending);

/// <summary>Per-status counts within a filtered PR search result.</summary>
public sealed record PRStatusCountsDto(
    int Open,
    int PartiallyDelivered,
    int FullyDelivered,
    int Completed);

/// <summary>A page of PR List results plus the total match count and status breakdown.</summary>
public sealed record PRSearchResultDto(
    IReadOnlyList<PRSummaryDto> Items,
    int TotalCount,
    int Page,
    int PageSize,
    PRStatusCountsDto StatusCounts);
