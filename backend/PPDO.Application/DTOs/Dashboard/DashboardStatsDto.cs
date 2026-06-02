namespace PPDO.Application.DTOs.Dashboard;

/// <summary>
/// Grouped stat card counts returned by <c>GET /api/dashboard/stats</c>.
/// Maps directly to the two stat card groups on the Inventory Dashboard (Penpot frame 04).
/// Stock-level counts (InStock / LowStock) require the full Inventory module — placeholders
/// until RAL-47 implements the stock computation service.
/// </summary>
public sealed class DashboardStatsDto
{
    // ── Purchase Requests group ────────────────────────────────────────────────

    public int TotalPRs { get; init; }
    public int OpenPRs { get; init; }
    public int PartiallyDeliveredPRs { get; init; }
    public int FullyDeliveredPRs { get; init; }

    // ── Items / Inventory group ────────────────────────────────────────────────

    /// <summary>Total distinct items in the Items Master catalog.</summary>
    public int TotalItems { get; init; }

    /// <summary>Items flagged IsNewItem = true, pending Admin review.</summary>
    public int NewItemsPendingReview { get; init; }
}
