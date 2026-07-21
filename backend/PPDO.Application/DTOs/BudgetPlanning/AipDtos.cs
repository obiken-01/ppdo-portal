namespace PPDO.Application.DTOs.BudgetPlanning;

// ── Read DTOs ─────────────────────────────────────────────────────────────────

public record AipActivityDto(
    int      Id,
    int      ProjectId,
    string   RefCode,
    string   Name,
    string?  EsreCode,
    string?  ImplementingOffice,
    string?  StartDate,
    string?  EndDate,
    string?  ExpectedOutputs,
    int?     FundingSourceId,
    string?  FundingSourceSnapshot,
    decimal? Ps,
    decimal? Mooe,
    decimal? Co,
    decimal? Total,
    decimal? CcAdaptation,
    decimal? CcMitigation,
    string?  CcTypologyCode,
    bool     IsCreation,
    bool     IsSynthetic = false);

public record AipProjectDto(
    int    Id,
    int    ProgramId,
    string RefCode,
    string Name,
    IReadOnlyList<AipActivityDto> Activities,
    bool   IsSynthetic = false);

public record AipProgramDto(
    int     Id,
    int     OfficeId,
    string  RefCode,
    string  Name,
    IReadOnlyList<AipProjectDto> Projects,
    string? FunctionBand);

public record AipOfficeDto(
    int    Id,
    int    AipRecordId,
    string RefCode,
    string Name,
    string Sector,
    IReadOnlyList<AipProgramDto> Programs);

public record AipRecordDto(
    int      Id,
    int      FiscalYear,
    string   EntrySource,
    string?  OriginalFilename,
    Guid     UploadedById,
    DateTime UploadedAt,
    string   Status,
    int?     LdipId,
    int?     SourceId,
    int      OfficeCount,
    string?  UploadedByName);

public record AipRecordDetailDto(
    int      Id,
    int      FiscalYear,
    string   EntrySource,
    string?  OriginalFilename,
    Guid     UploadedById,
    DateTime UploadedAt,
    string   Status,
    int?     LdipId,
    int?     SourceId,
    IReadOnlyList<AipOfficeDto> Offices,
    bool     HasWfpUsage = false);

// ── Import counts ─────────────────────────────────────────────────────────────

public record AipImportCountsDto(int Offices, int Programs, int Projects, int Activities);

// ── Preview / Confirm DTOs ───────────────────────────────────────────────────

/// <summary>
/// Returned by POST /api/budget-planning/aip/upload.
/// Contains the full parsed hierarchy so the client can echo it back on /confirm.
/// SectorOffices key = "GENERAL" | "SOCIAL" | "ECONOMIC" | "OTHERS".
/// </summary>
public record AipImportPreviewDto(
    int    FiscalYear,
    Dictionary<string, List<ParsedAipOfficeDto>> SectorOffices,
    AipImportCountsDto Counts,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Parsed (not yet persisted) office node returned in the preview and echoed back on confirm.
/// Mirrors <see cref="ParsedAipOffice"/> but as a JSON-serialisable DTO.
/// </summary>
public record ParsedAipOfficeDto(
    string RefCode,
    string Name,
    string Sector,
    List<ParsedAipProgramDto> Programs);

public record ParsedAipProgramDto(
    string RefCode,
    string Name,
    List<ParsedAipProjectDto> Projects,
    ParsedAipActivityDto? LineItem = null);

public record ParsedAipProjectDto(
    string RefCode,
    string Name,
    List<ParsedAipActivityDto> Activities,
    ParsedAipActivityDto? LineItem = null);

public record ParsedAipActivityDto(
    string   RefCode,
    string   Name,
    string?  EsreCode,
    string?  ImplementingOffice,
    string?  StartDate,
    string?  EndDate,
    string?  ExpectedOutputs,
    string?  FundingSourceRaw,
    decimal? Ps,
    decimal? Mooe,
    decimal? Co,
    decimal? Total,
    decimal? CcAdaptation,
    decimal? CcMitigation,
    string?  CcTypologyCode);

/// <summary>
/// Body of POST /api/budget-planning/aip/confirm.
/// The client sends back the exact SectorOffices payload returned by /upload.
/// <see cref="TargetRecordId"/> (RAL-178): when set, ConfirmImportAsync full-replaces that
/// existing record's hierarchy (re-upload a corrected file) instead of creating a new record.
/// </summary>
public record AipImportConfirmDto(
    int    FiscalYear,
    string OriginalFilename,
    int?   LdipId,
    Dictionary<string, List<ParsedAipOfficeDto>> SectorOffices,
    int?   TargetRecordId = null);

// ── Slim WFP-grid DTOs (RAL-89) ───────────────────────────────────────────────

/// <summary>
/// Minimal activity data for the WFP activity grid.
/// Omits EsreCode, ImplementingOffice, StartDate, EndDate, ExpectedOutputs,
/// CcAdaptation, CcMitigation, CcTypologyCode — none read by the WFP page.
/// Cuts the 1.2 MB full-detail payload to ~90 KB.
/// </summary>
public record AipActivitySummaryDto(
    int      Id,
    string   RefCode,
    string   Name,
    decimal? Ps,
    decimal? Mooe,
    decimal? Co,
    decimal? Total,
    int?     FundingSourceId,
    string?  FundingSourceSnapshot,
    bool     IsCreation);

public record AipProjectSummaryDto(
    int    Id,
    string RefCode,
    string Name,
    IReadOnlyList<AipActivitySummaryDto> Activities);

public record AipProgramSummaryDto(
    int     Id,
    string  RefCode,
    string  Name,
    IReadOnlyList<AipProjectSummaryDto> Projects,
    string? FunctionBand);

public record AipOfficeSummaryDto(
    int    Id,
    string RefCode,
    string Name,
    string Sector,
    IReadOnlyList<AipProgramSummaryDto> Programs);

public record AipRecordSummaryDto(
    int    Id,
    int    FiscalYear,
    IReadOnlyList<AipOfficeSummaryDto> Offices);

// ── Field update DTOs (v1.4 Q1/Q2 — captured during WFP data entry) ────────────

public record UpdateAipProgramFunctionBandDto(string? FunctionBand);

public record UpdateAipActivityIsCreationDto(bool IsCreation);
