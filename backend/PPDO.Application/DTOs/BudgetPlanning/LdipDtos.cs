namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>Slim list/row shape — no hierarchy (list endpoints stay light).</summary>
public record LdipRecordDto(
    int    Id,
    string RefCode,
    string Title,
    int    FiscalYearStart,
    int    FiscalYearEnd,
    string EntryMode,
    string Status,
    int?   SourceId,
    Guid   CreatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int?    OfficeId = null,
    string? OfficeName = null,
    int     ProgramCount = 0);

// ── Hierarchy (RAL-61) ───────────────────────────────────────────────────────

/// <summary>One program row. Budget is in thousands (₱000), like AIP totals.</summary>
public record LdipProgramDto(
    int     Id,
    string  RefCode,
    string  Name,
    decimal Budget);

/// <summary>
/// One sector group under a document. RefCode is the office-level AIP ref code for
/// that sector (server-computed); Name is the office/sub-office display name, which
/// may differ per sector while sharing the same config office.
/// </summary>
public record LdipOfficeGroupDto(
    int    Id,
    string RefCode,
    string Name,
    string Sector,
    IReadOnlyList<LdipProgramDto> Programs);

/// <summary>Detail shape returned by GetById — record fields + full hierarchy.</summary>
public record LdipRecordDetailDto(
    int    Id,
    string RefCode,
    string Title,
    int    FiscalYearStart,
    int    FiscalYearEnd,
    string EntryMode,
    string Status,
    int?   SourceId,
    Guid   CreatedById,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int?    OfficeId,
    string? OfficeName,
    IReadOnlyList<LdipOfficeGroupDto> Groups);

// ── Write DTOs ────────────────────────────────────────────────────────────────

/// <summary>Program payload — ref codes are never client-supplied; the server computes them.</summary>
public record SaveLdipProgramDto(string Name, decimal Budget);

/// <summary>Sector-group payload. Sector must be General/Social/Economic/Others.</summary>
public record SaveLdipGroupDto(
    string Sector,
    string Name,
    IReadOnlyList<SaveLdipProgramDto> Programs);

/// <summary>
/// Create payload. Title may be blank — the server auto-generates
/// "LDIP {start}-{end} — {office name}". OfficeId/Groups have defaults so
/// pre-RAL-61 call sites keep compiling, but OfficeId is required at the service.
/// </summary>
public record CreateLdipDto(
    string Title,
    int    FiscalYearStart,
    int    FiscalYearEnd,
    string EntryMode,
    int?   OfficeId = null,
    IReadOnlyList<SaveLdipGroupDto>? Groups = null);

/// <summary>Update payload — full-replace of the hierarchy (ref codes recomputed).</summary>
public record UpdateLdipDto(
    string Title,
    int    FiscalYearStart,
    int    FiscalYearEnd,
    string EntryMode,
    int?   OfficeId = null,
    IReadOnlyList<SaveLdipGroupDto>? Groups = null);
