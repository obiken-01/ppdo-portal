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
    string?  CcTypologyCode);

public record AipProjectDto(
    int    Id,
    int    ProgramId,
    string RefCode,
    string Name,
    IReadOnlyList<AipActivityDto> Activities);

public record AipProgramDto(
    int    Id,
    int    OfficeId,
    string RefCode,
    string Name,
    IReadOnlyList<AipProjectDto> Projects);

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
    int?     SourceId);

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
    IReadOnlyList<AipOfficeDto> Offices);

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
    List<ParsedAipProjectDto> Projects);

public record ParsedAipProjectDto(
    string RefCode,
    string Name,
    List<ParsedAipActivityDto> Activities);

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
/// </summary>
public record AipImportConfirmDto(
    int    FiscalYear,
    string OriginalFilename,
    int?   LdipId,
    Dictionary<string, List<ParsedAipOfficeDto>> SectorOffices);
