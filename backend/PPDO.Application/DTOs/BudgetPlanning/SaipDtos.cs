namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One SAIP, as the history list and the SAIP banner show it (v1.10.0 — PPDO-246, spec §4).
/// Slim on purpose: no tree, no diff — those have their own endpoints (T2/T3).
/// </summary>
/// <param name="DraftAipOfficeIds">
/// The draft copy's group rows while the SAIP is open; empty once it is Approved or Cancelled.
/// </param>
public sealed record SaipDto(
    int       Id,
    int       AipRecordId,
    int       FiscalYear,
    int       OfficeId,
    int       Number,
    string    Status,
    bool      IsOpen,
    DateTime  StartedAt,
    Guid      StartedById,
    string?   StartedByName,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    DateTime? CancelledAt,
    string?   CancelledByName,
    string?   LetterFileName,
    IReadOnlyList<int> DraftAipOfficeIds);
