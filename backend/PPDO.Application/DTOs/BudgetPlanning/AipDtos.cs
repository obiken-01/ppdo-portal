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
    /// <summary>
    /// The config <c>offices</c> row that owns this group (V18-32), added by PPDO-52.
    ///
    /// ⚠️ <b>The entry page needs it and could not be written correctly without it.</b> A
    /// host-office user legitimately receives every office in the record, so with no owner on the
    /// DTO the page rendered all 25 offices' trees while its checklist covered only the caller's
    /// own — an encoder saw someone else's programs above a panel saying "0 activities in this
    /// office". Found by live-testing, not review.
    ///
    /// Null only for an unmatched legacy row, exactly as on <c>AipOffice.OfficeId</c>.
    /// </summary>
    int?   OfficeId,
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

// ── Manual entry (RAL-62) ────────────────────────────────────────────────────
// One node at a time — mirrors the "Entry Level" tabs (Office/Program/Project/Activity) UI.
// Each Add* call persists immediately; ref codes are auto-derived server-side, never supplied
// by the client (see AipService.NextRefCode / the AipSector prefix map).

/// <summary>Body of POST /api/budget-planning/aip — creates a blank Manual-entry AipRecord.</summary>
/// <summary>
/// Body of POST /api/budget-planning/aip — opens a fiscal year (PPDO-62). Admin/SuperAdmin only.
///
/// <para>
/// ↩️ V18-40's <c>OfficeConfigId</c> was removed (PPDO-61): it chose a record shape, and there is
/// only one. <b>One base record per fiscal year holds every office</b>, so the year is all this
/// needs.
/// </para>
/// </summary>
public record OpenAipFiscalYearDto(int FiscalYear);

/// <summary>
/// What opening a fiscal year did (PPDO-62).
/// </summary>
/// <param name="Record">The base record that now holds every office.</param>
/// <param name="OfficesPopulated">How many <c>AipOffice</c> rows were created across all sectors.</param>
/// <param name="OfficesWithoutLdip">
/// ⚠️ <b>Names of active offices that got nothing, because they have no LDIP in any sector.</b>
/// This is a real output, not a nicety: such an office cannot build its AIP and has no way to
/// discover why — it opens the page and finds nothing. Surface this list; do not swallow it
/// because the operation "succeeded".
/// </param>
public record OpenAipFiscalYearResultDto(
    AipRecordDto Record,
    int OfficesPopulated,
    IReadOnlyList<string> OfficesWithoutLdip);

/// <summary>
/// Body of POST /api/budget-planning/aip/{aipId}/offices. <see cref="OfficeConfigId"/> is the
/// config <c>Office</c> row (RefCode is always derived from it; <see cref="Name"/> defaults to
/// its <c>OfficeName</c> but can be overridden — real AIP files often label a sub-office/program
/// cluster under the same physical office differently, e.g. "PROVINCIAL PLANNING AND DEVELOPMENT
/// OFFICE - SPECIAL PROJECTS" or "OFFICE OF THE GOVERNOR - WARDEN", sharing the same RefCode as
/// the office's other entries but with a distinguishing name). <see cref="Sector"/> is a separate
/// choice — the same office can appear under more than one sector.
/// </summary>
public record CreateAipOfficeDto(int OfficeConfigId, string Sector, string? Name = null);

/// <summary>
/// RAL-181 — seed an AIP office's programs (Name+RefCode only, no Project/Activity rows) from
/// that office's existing LDIP for <see cref="Sector"/>. Target AipRecord/AipOffice are
/// found-or-created by the service.
/// </summary>
public record SeedAipProgramsFromLdipDto(
    int TargetFiscalYear, int OfficeConfigId, string Sector, IReadOnlyList<int> LdipProgramIds);

/// <summary>
/// Body of <c>POST /api/budget-planning/aip/{aipId}/programs</c> (V18-42 / PPDO-52, spec §4).
///
/// <b>The sub-office group and the programs arrive together because they are one interaction</b>
/// (§2 decision 2). The encoder picks a sector, names the group, ticks programs from the LDIP and
/// presses Add — exactly what <c>LdipForm.tsx</c> already does, which PPDO-52 says to lift rather
/// than redesign.
///
/// <para>
/// ⚠️ <see cref="GroupName"/> is what this endpoint exists for. <c>SeedProgramsFromLdipAsync</c>
/// finds its target <c>AipOffice</c> by <b>ref code alone</b> and takes the name from the LDIP, so
/// it can only ever reach the FIRST group under a ref code — it cannot start a second. Real AIPs
/// need several: the province's FY2027 SOCIAL sheet carries three <c>3000-000-1-01-001</c> office
/// rows (<i>OFFICE OF THE GOVERNOR - WARDEN</i>, <i>- AKAP-HUB</i>, <i>- HOUSING</i>), each heading
/// its own block with its own shaded subtotal.
/// </para>
///
/// <para>
/// ⚠️ The group is <b>not</b> the division. It is the <c>(Sector, Name)</c> pair on
/// <c>AipOffice</c>, it <b>prints</b>, and it applies to every office. A division is
/// <c>ProgramDivision</c>, host-office only, and never prints (spec §3.1).
/// </para>
///
/// <para>
/// Null or blank <see cref="GroupName"/> means "the office's default group" and takes the LDIP
/// group's own name, which is what an office with no sub-offices wants.
/// </para>
/// </summary>
public record AddAipProgramsWithGroupDto(
    int                 OfficeConfigId,
    string              Sector,
    string?             GroupName,
    IReadOnlyList<int>  LdipProgramIds);

/// <summary>
/// One LDIP program an office may add to its AIP (V18-42 / PPDO-52).
///
/// <see cref="LdipProgramId"/> is the id <c>AddAipProgramsWithGroupDto.LdipProgramIds</c> expects —
/// deliberately named for what it is, because it is NOT the AIP program's id and confusing the two
/// produces a "does not belong to this office's LDIP" refusal that looks like a permissions bug.
/// </summary>
public record AipAddableProgramDto(int LdipProgramId, string RefCode, string Name);

/// <summary>
/// What an office may add for one sector, resolved <b>server-side</b> (V18-42 / PPDO-52).
///
/// ⚠️ <b>This endpoint exists because the client must not resolve the LDIP itself.</b>
/// <c>ResolveLdipGroupAsync</c> is two-tier — the office's own LDIP first, then a multi-office bulk
/// LDIP matched on ref code — and its own remarks already noted the frontend mirrored it a third
/// time. A fourth copy in the entry panel diverged in practice: the panel offered programs from one
/// LDIP record while the server resolved a different one, and every add was refused with
/// <i>"LDIP program id(s) … do not belong to this office's GENERAL LDIP"</i>. Found by live-testing.
///
/// Both halves were individually correct. Serving the list from the same resolver the write path
/// uses makes them agree by construction rather than by two teams keeping two copies in step.
/// </summary>
/// <param name="LdipRefCode">
/// The LDIP record these programs come from.
///
/// ⚠️ Returned so the encoder can answer "where did these come from?" without leaving the page.
/// The resolver is two-tier and its second tier is a <b>multi-office</b> LDIP owned by no single
/// office — which the LDIP list could not show anyone until this ticket. Naming the source here is
/// what makes the closed list explicable rather than mysterious.
/// </param>
/// <param name="LdipTitle">That record's title, for humans.</param>
/// <param name="IsSharedLdip">
/// True when the source is a multi-office LDIP rather than this office's own. Worth surfacing:
/// it explains why the record may not look like "your" LDIP.
/// </param>
public record AipAddableProgramsDto(
    string  GroupRefCode,
    string  GroupName,
    string? LdipRefCode,
    string? LdipTitle,
    bool    IsSharedLdip,
    IReadOnlyList<AipAddableProgramDto> Programs);

public record CreateAipProgramDto(string Name, string? FunctionBand = null);

public record CreateAipProjectDto(string Name);

public record CreateAipActivityDto(
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
    decimal? CcAdaptation,
    decimal? CcMitigation,
    string?  CcTypologyCode);

// ── Inline activity edit (RAL-179) ───────────────────────────────────────────

/// <summary>
/// Body of PUT /api/budget-planning/aip/{id}/activities/{activityId}. Editable field set only —
/// RefCode, ProjectId, and activity identity are immutable through this endpoint (moving a node
/// is a structural change, not a field correction). <see cref="FundingSourceId"/> is a direct FK
/// to a config FundingSource row (not a raw code string like <see cref="CreateAipActivityDto"/>'s
/// import-time matching) — the UI offers a dropdown of known sources, so there's nothing to match.
/// </summary>
public record UpdateAipActivityDto(
    string   Name,
    string?  EsreCode,
    string?  ImplementingOffice,
    string?  StartDate,
    string?  EndDate,
    string?  ExpectedOutputs,
    int?     FundingSourceId,
    decimal? Ps,
    decimal? Mooe,
    decimal? Co,
    decimal? CcAdaptation,
    decimal? CcMitigation,
    string?  CcTypologyCode);

// ── Inline office/program/project edit (detail-page CRUD follow-up to RAL-179) ─
// RefCode/Sector/hierarchy position stay immutable through these endpoints, same
// principle as UpdateAipActivityDto — only the human-facing fields are editable.

/// <summary>Body of PUT /api/budget-planning/aip/offices/{officeId}. Only Name is editable —
/// RefCode/Sector stay fixed (changing sector would change the ref-code prefix, a structural
/// move, not a field correction).</summary>
public record UpdateAipOfficeDto(string Name);

/// <summary>Body of PUT /api/budget-planning/aip/programs/{programId}. Separate from the
/// narrower PUT .../programs/{id}/function-band endpoint (v1.4 WFP entry context picker,
/// unchanged) — this one is the detail page's full name+band edit.</summary>
public record UpdateAipProgramDto(string Name, string? FunctionBand);

/// <summary>Body of PUT /api/budget-planning/aip/projects/{projectId}. Only Name is editable.</summary>
public record UpdateAipProjectDto(string Name);

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
