namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// Lightweight PR record for list and filter views.
/// Includes all fields that the PR List filter panel operates on so the
/// frontend can do fast client-side filtering on a single API response.
/// </summary>
public sealed record PRSummaryDto(
    Guid     Id,
    string   PRNo,
    DateOnly PRDate,
    /// <summary>Division name string e.g. "Admin" — not the enum integer.</summary>
    string   Division,
    string   RequestedBy,
    decimal  TotalAmount,
    string   Status,
    DateTime CreatedAt,
    // ── Filter fields ────────────────────────────────────────────────────────
    string   Fund,
    string?  AIPCode,
    string?  AccountNo,
    string?  AccountTitle,
    string?  Program,
    string?  Project,
    string?  Activity);
