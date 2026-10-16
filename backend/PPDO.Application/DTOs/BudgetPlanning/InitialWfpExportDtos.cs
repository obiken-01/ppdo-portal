namespace PPDO.Application.DTOs.BudgetPlanning;

// v1.9.0 — PPDO-228. The bulk initial WFP export (docs/v1.9/Bulk_Initial_WFP_Export_Spec.md §4).

/// <summary>Body of <c>POST /api/budget-planning/wfp/report/bulk-exports</c>.</summary>
public sealed record RequestInitialWfpExportDto(int FiscalYear);

/// <summary>
/// An export job as the Report page sees it (spec §4). <see cref="Status"/> is one of
/// <c>Queued | Running | Done | DoneWithProblems | Failed | Expired</c>.
/// </summary>
public sealed record InitialWfpExportJobDto(
    int Id,
    int FiscalYear,
    string Status,
    int Done,
    int Total,
    IReadOnlyList<InitialWfpExportProblemDto> Problems,
    string? Message,
    string RequestedByName,
    DateTime CreatedAt,
    DateTime? FinishedAt,
    DateTime? ExpiresAt);

/// <summary>An office the export skipped, and why (spec decision 7).</summary>
public sealed record InitialWfpExportProblemDto(string OfficeCode, string Reason);

/// <summary>What a request produced: the job, and whether it is new (202) or the year's active one (200).</summary>
public sealed record InitialWfpExportRequestResult(InitialWfpExportJobDto Job, bool Created);

/// <summary>A finished export, opened for download. The caller disposes <see cref="Content"/>.</summary>
public sealed record InitialWfpExportDownload(Stream Content, string FileName);
