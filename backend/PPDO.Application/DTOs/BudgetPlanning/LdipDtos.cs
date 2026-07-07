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

/// <summary>
/// One program row. Budget is in thousands (₱000), like AIP totals.
/// The detail fields below (RAL-113) are populated only for upload-derived
/// programs — null for programs added through the manual "+ Add Program" flow.
/// </summary>
public record LdipProgramDto(
    int     Id,
    string  RefCode,
    string  Name,
    decimal Budget,
    string?  ImplementingOffice = null,
    string?  StartDate = null,
    string?  EndDate = null,
    string?  ExpectedOutputs = null,
    int?     FundingSourceId = null,
    string?  FundingSourceSnapshot = null,
    decimal? Ps = null,
    decimal? Mooe = null,
    decimal? Co = null,
    decimal? CcAdaptation = null,
    decimal? CcMitigation = null,
    string?  CcTypologyCode = null,
    string?  PdpRdp = null,
    string?  Sdgs = null,
    string?  SendaiFramework = null,
    string?  NdrrmPlan = null,
    string?  Nsp = null,
    string?  Pdpdfp = null);

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

/// <summary>
/// Program payload — ref codes are never client-supplied; the server computes them.
/// The detail fields below (RAL-113) are only ever set by the upload-confirm flow
/// (echoed back from the preview response) — the manual "+ Add Program" form only
/// ever sends Name/Budget, leaving the rest null. FundingSourceRaw is the file's
/// text value; the server resolves it to a FundingSourceId/snapshot at save time.
/// </summary>
public record SaveLdipProgramDto(
    string   Name,
    decimal  Budget,
    string?  ImplementingOffice = null,
    string?  StartDate = null,
    string?  EndDate = null,
    string?  ExpectedOutputs = null,
    string?  FundingSourceRaw = null,
    decimal? Ps = null,
    decimal? Mooe = null,
    decimal? Co = null,
    decimal? CcAdaptation = null,
    decimal? CcMitigation = null,
    string?  CcTypologyCode = null,
    string?  PdpRdp = null,
    string?  Sdgs = null,
    string?  SendaiFramework = null,
    string?  NdrrmPlan = null,
    string?  Nsp = null,
    string?  Pdpdfp = null);

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

// ── File upload (RAL-113) ────────────────────────────────────────────────────
// The workbook covers every office in one file — there is no office picker.
// Upload parses the whole file, matches every office block found (across all 4
// sectors) to a Config → Offices record by AIP ref code, and Confirm creates one
// Draft LdipRecord PER OFFICE detected, all in a single batch.

/// <summary>One config office's matched slice of the uploaded file.</summary>
public record LdipImportOfficeResultDto(
    int    OfficeId,
    string OfficeCode,
    string OfficeName,
    IReadOnlyList<SaveLdipGroupDto> Groups);

/// <summary>Import counts shown on the preview page.</summary>
public record LdipImportCountsDto(int Offices, int Groups, int Programs);

/// <summary>
/// Returned by POST /api/budget-planning/ldip/upload. Each entry in Offices is the
/// same shape as the manual-entry save payload, so the client can echo the whole
/// list straight back to /confirm.
/// </summary>
public record LdipImportPreviewDto(
    int    FiscalYearStart,
    int    FiscalYearEnd,
    IReadOnlyList<LdipImportOfficeResultDto> Offices,
    LdipImportCountsDto Counts,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Body of POST /api/budget-planning/ldip/confirm — echoes back the preview's Offices.
/// <para>
/// RAL-114: when <see cref="TargetRecordId"/> is set, the confirm full-replaces that
/// existing record's hierarchy (re-upload a corrected file) instead of creating a new
/// record — reusing the same document's Id/RefCode/CreatedAt. It must be a Draft,
/// Upload-entry-mode record. When null (the default), a new record is created — the
/// original, fully backward-compatible behaviour.
/// </para>
/// </summary>
public record LdipImportConfirmDto(
    int    FiscalYearStart,
    int    FiscalYearEnd,
    IReadOnlyList<LdipImportOfficeResultDto> Offices,
    int?   TargetRecordId = null);
