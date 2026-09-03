using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// AIP service — XLSM upload, confirm-import, and status lifecycle (RAL-64).
/// Hierarchy: AipRecord → AipOffice → AipProgram → AipProject → AipActivity.
/// Confirm is stateless: the client echoes back the full preview payload.
/// Snapshot columns (FundingSourceId/Snapshot) are populated at confirm time
/// by matching FundingSourceRaw against the config table.
///
/// RAL-93: hierarchy reads now use IAipRepository scoped queries (WHERE / IN in SQL)
/// instead of loading full tables and filtering in memory.
/// </summary>
public sealed class AipService : IAipService
{
    private readonly IAipRepository            _aipRepo;
    private readonly IRepository<FundingSource> _fsRepo;
    private readonly IUserRepository           _userRepo;
    private readonly IAipXlsmParser _parser;
    private readonly IAuditService  _audit;
    private readonly CallerContext  _caller;
    private readonly IRepository<AipOffice> _officeRepo;
    private readonly IWfpRepository _wfpRepo;
    private readonly IOfficeRepository _officeConfigRepo;
    private readonly IRepository<AipProgram>  _programRepo;
    private readonly IRepository<AipProject>  _projectRepo;
    private readonly IRepository<AipActivity> _activityRepo;
    private readonly ILdipRepository _ldipRepo;
    private readonly IAllocationRepository _allocationRepo;

    public AipService(
        IAipRepository             aipRepo,
        IRepository<FundingSource>  fsRepo,
        IUserRepository            userRepo,
        IAipXlsmParser parser,
        IAuditService  audit,
        CallerContext  caller,
        IRepository<AipOffice> officeRepo,
        IWfpRepository wfpRepo,
        IOfficeRepository officeConfigRepo,
        IRepository<AipProgram>  programRepo,
        IRepository<AipProject>  projectRepo,
        IRepository<AipActivity> activityRepo,
        ILdipRepository ldipRepo,
        IAllocationRepository allocationRepo)
    {
        _aipRepo    = aipRepo;
        _fsRepo     = fsRepo;
        _userRepo   = userRepo;
        _parser     = parser;
        _audit      = audit;
        _caller     = caller;
        _officeRepo = officeRepo;
        _wfpRepo    = wfpRepo;
        _officeConfigRepo = officeConfigRepo;
        _programRepo      = programRepo;
        _projectRepo      = projectRepo;
        _activityRepo     = activityRepo;
        _ldipRepo         = ldipRepo;
        _allocationRepo   = allocationRepo;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// The host office's <see cref="ProgramDivision"/> rows, loaded ONLY when the division axis
    /// will actually narrow something (V18-39). A guest-office caller can never be narrowed by
    /// division, so issuing this query for them would be a round trip whose result is discarded.
    /// </summary>
    private async Task<IReadOnlyList<ProgramDivision>> LoadHostAssignmentsAsync(
        AipReadScope scope, CancellationToken ct)
        => scope.HostOfficeIdForAssignments is int hostOfficeId
            ? await _allocationRepo.GetProgramDivisionsByOfficeIdAsync(hostOfficeId, ct)
            : [];

    public async Task<IReadOnlyList<AipRecordDto>> GetAllAsync(
        int? fiscalYear, string? status, User caller, CancellationToken ct = default)
    {
        IEnumerable<AipRecord> q = await _aipRepo.GetAllAsync(ct);
        if (fiscalYear.HasValue) q = q.Where(r => r.FiscalYear == fiscalYear.Value);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));

        List<AipRecord> records = q.OrderByDescending(r => r.UploadedAt).ToList();

        // Scope office count to only the AIP ids being returned (not the whole table).
        List<int> aipIds = records.Select(r => r.Id).ToList();
        IReadOnlyList<AipOffice> allOffices = await _aipRepo.GetOfficesByAipIdsAsync(aipIds, ct);

        // ...and then to the offices this caller may see (V18-39). Without it a guest office is
        // told the record contains 37 offices when it can open exactly one of them — a misleading
        // count rather than a data leak, but it comes from the same unscoped read.
        IReadOnlyList<AipOffice> offices = AipReadScope.Resolve(caller).FilterOffices(allOffices);

        Dictionary<int, int> officeCounts = offices
            .GroupBy(o => o.AipRecordId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Build user name lookup scoped to only the uploader ids in this result set.
        List<Guid> uploaderIds = records.Select(r => r.UploadedById).Distinct().ToList();
        IReadOnlyDictionary<Guid, string> userNames = await _userRepo.GetNamesByIdsAsync(uploaderIds, ct);

        return records.Select(r => MapToListDto(r, officeCounts, userNames)).ToList();
    }

    public async Task<ServiceResult<AipRecordDetailDto>> GetByIdAsync(
        int id, User caller, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<AipRecordDetailDto>.NotFound($"AIP record {id} not found.");

        // Load each hierarchy level scoped to the ids from the level above.
        IReadOnlyList<AipOffice> allOffices = await _aipRepo.GetOfficesByAipIdAsync(id, ct);

        // ⚠️ V18-39 — until this ticket, this endpoint returned EVERY office's full AIP hierarchy
        // to any caller with Budget Planning access. No production guest-office accounts existed
        // yet, which is the only reason that was not a live leak.
        AipReadScope scope = AipReadScope.Resolve(caller);
        IReadOnlyList<AipOffice> offices = scope.FilterOffices(allOffices);

        List<int> officeIds  = offices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram> allPrograms = await _aipRepo.GetProgramsByOfficeIdsAsync(officeIds, ct);
        IReadOnlyList<AipProgram> programs = scope.FilterPrograms(
            allPrograms, offices, await LoadHostAssignmentsAsync(scope, ct));
        List<int> programIds = programs.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject>  projects = await _aipRepo.GetProjectsByProgramIdsAsync(programIds, ct);
        List<int> projectIds = projects.Select(j => j.Id).ToList();
        IReadOnlyList<AipActivity> acts     = await _aipRepo.GetActivitiesByProjectIdsAsync(projectIds, ct);

        // Build nested DTO hierarchy.
        IReadOnlyList<AipOfficeDto> officeDtos = offices.Select(o =>
        {
            IReadOnlyList<AipProgramDto> progDtos = programs
                .Where(p => p.OfficeId == o.Id)
                .Select(p =>
                {
                    IReadOnlyList<AipProjectDto> projDtos = projects
                        .Where(j => j.ProgramId == p.Id)
                        .Select(j => new AipProjectDto(j.Id, j.ProgramId, j.RefCode, j.Name,
                            acts.Where(a => a.ProjectId == j.Id).Select(MapActivityToDto).ToList(),
                            j.IsSynthetic))
                        .ToList();
                    return new AipProgramDto(p.Id, p.OfficeId, p.RefCode, p.Name, projDtos, p.FunctionBand);
                })
                .ToList();
            return new AipOfficeDto(o.Id, o.AipRecordId, o.RefCode, o.Name, o.Sector, progDtos);
        }).ToList();

        // Drives the frontend's Re-upload button gating — see ReplaceImportAsync's guard below.
        bool hasWfpUsage = await _wfpRepo.AnyForAipRecordAsync(id, ct);

        AipRecordDetailDto detail = new(
            rec.Id, rec.FiscalYear, rec.EntrySource, rec.OriginalFilename,
            rec.UploadedById, rec.UploadedAt, rec.Status, rec.LdipId, rec.SourceId, officeDtos,
            hasWfpUsage);

        return ServiceResult<AipRecordDetailDto>.Ok(detail);
    }

    public async Task<ServiceResult<AipRecordSummaryDto>> GetSummaryByIdAsync(
        int id, User caller, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<AipRecordSummaryDto>.NotFound($"AIP record {id} not found.");

        // Same two-axis scope as GetByIdAsync — this is the grid the detail page actually renders,
        // so leaving it unscoped would defeat scoping the heavier sibling (V18-39).
        AipReadScope scope = AipReadScope.Resolve(caller);
        IReadOnlyList<AipOffice> offices =
            scope.FilterOffices(await _aipRepo.GetOfficesByAipIdAsync(id, ct));

        List<int> officeIds  = offices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram> programs = scope.FilterPrograms(
            await _aipRepo.GetProgramsByOfficeIdsAsync(officeIds, ct),
            offices,
            await LoadHostAssignmentsAsync(scope, ct));
        List<int> programIds = programs.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject>  projects = await _aipRepo.GetProjectsByProgramIdsAsync(programIds, ct);
        List<int> projectIds = projects.Select(j => j.Id).ToList();
        IReadOnlyList<AipActivity> acts     = await _aipRepo.GetActivitiesByProjectIdsAsync(projectIds, ct);

        IReadOnlyList<AipOfficeSummaryDto> officeDtos = offices.Select(o =>
        {
            IReadOnlyList<AipProgramSummaryDto> progDtos = programs
                .Where(p => p.OfficeId == o.Id)
                .Select(p =>
                {
                    IReadOnlyList<AipProjectSummaryDto> projDtos = projects
                        .Where(j => j.ProgramId == p.Id)
                        .Select(j => new AipProjectSummaryDto(j.Id, j.RefCode, j.Name,
                            acts.Where(a => a.ProjectId == j.Id)
                                .Select(a => new AipActivitySummaryDto(
                                    a.Id, a.RefCode, a.Name,
                                    a.Ps, a.Mooe, a.Co, a.Total,
                                    a.FundingSourceId, a.FundingSourceSnapshot, a.IsCreation))
                                .ToList()))
                        .ToList();
                    return new AipProgramSummaryDto(p.Id, p.RefCode, p.Name, projDtos, p.FunctionBand);
                })
                .ToList();
            return new AipOfficeSummaryDto(o.Id, o.RefCode, o.Name, o.Sector, progDtos);
        }).ToList();

        return ServiceResult<AipRecordSummaryDto>.Ok(new AipRecordSummaryDto(rec.Id, rec.FiscalYear, officeDtos));
    }

    // ── Preview ───────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipImportPreviewDto>> ParsePreviewAsync(
        Stream xlsmStream,
        int fiscalYear,
        IReadOnlyList<FundingSource> knownFundingSources,
        CancellationToken ct = default)
    {
        Dictionary<string, List<ParsedAipOffice>> parsed;
        try
        {
            parsed = _parser.Parse(xlsmStream);
        }
        catch (AipParseException ex)
        {
            return ServiceResult<AipImportPreviewDto>.BadRequest(string.Join("; ", ex.Errors));
        }

        Dictionary<string, FundingSource> fsDict =
            knownFundingSources.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);

        List<string> warnings = [];
        int officeCount = 0, programCount = 0, projectCount = 0, activityCount = 0;
        Dictionary<string, List<ParsedAipOfficeDto>> sectorDtos =
            new(StringComparer.OrdinalIgnoreCase);

        // RAL-108: shared mapping for both real activity rows and program/project LineItems —
        // both become a real AipActivity row at confirm time, so both count toward activityCount
        // and both get the unmatched-funding-source warning.
        ParsedAipActivityDto MapActivity(ParsedAipActivity act)
        {
            activityCount++;
            if (!string.IsNullOrWhiteSpace(act.FundingSourceRaw) &&
                !fsDict.ContainsKey(act.FundingSourceRaw))
            {
                warnings.Add($"Activity {act.RefCode}: unmatched funding source '{act.FundingSourceRaw}'.");
            }
            return new ParsedAipActivityDto(
                act.RefCode, act.Name, act.EsreCode, act.ImplementingOffice,
                act.StartDate, act.EndDate, act.ExpectedOutputs, act.FundingSourceRaw,
                act.Ps, act.Mooe, act.Co, act.Total,
                act.CcAdaptation, act.CcMitigation, act.CcTypologyCode);
        }

        foreach ((string sector, List<ParsedAipOffice> offices) in parsed)
        {
            List<ParsedAipOfficeDto> officeDtos = [];
            foreach (ParsedAipOffice off in offices)
            {
                officeCount++;
                List<ParsedAipProgramDto> progDtos = [];
                foreach (ParsedAipProgram prog in off.Programs)
                {
                    programCount++;
                    List<ParsedAipProjectDto> projDtos = [];
                    foreach (ParsedAipProject proj in prog.Projects)
                    {
                        projectCount++;
                        List<ParsedAipActivityDto> actDtos = proj.Activities.Select(MapActivity).ToList();
                        ParsedAipActivityDto? projLineItem = proj.LineItem is null ? null : MapActivity(proj.LineItem);
                        projDtos.Add(new ParsedAipProjectDto(proj.RefCode, proj.Name, actDtos, projLineItem));
                    }
                    ParsedAipActivityDto? progLineItem = null;
                    if (prog.LineItem is not null)
                    {
                        // A program-level line item is materialized as a synthetic child project
                        // at confirm time — count it here so preview counts match what gets saved.
                        projectCount++;
                        progLineItem = MapActivity(prog.LineItem);
                    }
                    progDtos.Add(new ParsedAipProgramDto(prog.RefCode, prog.Name, projDtos, progLineItem));
                }
                officeDtos.Add(new ParsedAipOfficeDto(off.RefCode, off.Name, off.Sector, progDtos));
            }
            sectorDtos[sector] = officeDtos;
        }

        AipImportPreviewDto preview = new(
            fiscalYear, sectorDtos,
            new AipImportCountsDto(officeCount, programCount, projectCount, activityCount),
            warnings.AsReadOnly());

        return ServiceResult<AipImportPreviewDto>.Ok(preview);
    }

    // ── Confirm import ────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipRecordDto>> ConfirmImportAsync(
        AipImportConfirmDto dto, Guid uploadedById, CancellationToken ct = default)
    {
        // ⚠️ V18-37 — the workbook carries every office in one file, so an import is legacy-shaped
        // by construction and there is no year from FY2028 on that can accept one. V18-38 disables
        // the button; this is the server-side half and has to stand on its own, because a disabled
        // button is a courtesy and not a guard. Placed before the re-upload branch too: replacing
        // a record's hierarchy from a workbook is just as shape-bound as creating one.
        if (AipShape.Mismatch(dto.FiscalYear, officeId: null) is string shapeError)
            return ServiceResult<AipRecordDto>.BadRequest(shapeError);

        // Load funding source lookup for snapshot population — needed by both paths below.
        IReadOnlyList<FundingSource> fsList = await _fsRepo.GetAllAsync(ct);
        Dictionary<string, FundingSource> fsDict =
            fsList.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);

        // Config offices, loaded once, to resolve each uploaded office's ownership FK (V18-32).
        // Sequential with the read above, not Task.WhenAll — they share one DbContext.
        IReadOnlyList<Office> configOffices = await _officeConfigRepo.GetAllAsync(ct);

        // Re-upload path (RAL-178) — replace an existing record's hierarchy in place.
        // Bypasses the one-active-AIP-per-fiscal-year guard below entirely: that guard exists
        // to stop a SECOND competing record for the year, not the record being replaced (which
        // GetLatestByFiscalYearAsync would otherwise find as a false "conflict" — itself).
        if (dto.TargetRecordId is int targetId)
            return await ReplaceImportAsync(targetId, dto, fsDict, configOffices, ct);

        // Guard: only one active (Draft or Final) AIP per fiscal year.
        AipRecord? conflict = await _aipRepo.GetLatestByFiscalYearAsync(dto.FiscalYear, ct);
        if (conflict is not null)
        {
            string hint = conflict.Status == PlanningStatus.Draft
                ? "Archive the existing record first before uploading a new one."
                : "The existing record must be unlocked by an admin before a new upload is allowed.";
            return ServiceResult<AipRecordDto>.BadRequest(
                $"An AIP for FY {dto.FiscalYear} already exists with status '{conflict.Status}'. {hint}");
        }

        DateTime now = DateTime.UtcNow;

        // Build the full entity graph in memory so EF Core inserts it in a single
        // SaveChangesAsync (one implicit transaction). Previously each hierarchy
        // level called SaveChangesAsync individually to obtain generated IDs, so a
        // failure at the activity level (e.g. column truncation) left committed
        // orphan rows for offices/programs/projects. Navigation properties let EF
        // Core resolve all FK assignments without intermediate saves.
        AipRecord aipRecord = new()
        {
            FiscalYear       = dto.FiscalYear,
            EntrySource      = "Upload",
            OriginalFilename = dto.OriginalFilename,
            UploadedById     = uploadedById,
            UploadedAt       = now,
            Status           = PlanningStatus.Draft,
            LdipId           = dto.LdipId,
            Offices          = BuildOffices(dto.SectorOffices, fsDict, configOffices),
        };

        await _aipRepo.AddAsync(aipRecord, ct);
        await _aipRepo.SaveChangesAsync(ct); // single transaction — all-or-nothing

        await _audit.LogAsync("aip_records", aipRecord.Id, AuditAction.Create,
            null, new { aipRecord.FiscalYear, aipRecord.EntrySource, aipRecord.Status }, ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(aipRecord));
    }

    /// <summary>
    /// RAL-178 — re-upload a corrected file into an EXISTING record. Full-replaces the
    /// hierarchy in two SaveChanges rounds (delete existing top-level AipOffice rows first,
    /// then insert the freshly parsed ones) so the ref-code indexes never see old+new rows
    /// side by side. DB-level cascade (AipRecord -&gt; AipOffice -&gt; AipProgram -&gt; AipProject
    /// -&gt; AipActivity, all DeleteBehavior.Cascade) removes each old office's whole subtree
    /// once its top-level row is deleted — no need to load the deep tree first.
    ///
    /// Id/UploadedById/original creation semantics are preserved — same document, corrected
    /// content, audit trail intact; only OriginalFilename, UploadedAt, and the hierarchy change.
    /// Guards: the target must exist, be Draft, and be an Upload-entry-source record.
    /// Logged as an Update (not a Create).
    /// </summary>
    private async Task<ServiceResult<AipRecordDto>> ReplaceImportAsync(
        int targetId, AipImportConfirmDto dto, Dictionary<string, FundingSource> fsDict,
        IReadOnlyList<Office> configOffices, CancellationToken ct)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(targetId, ct);
        if (rec is null)
            return ServiceResult<AipRecordDto>.NotFound($"AIP record {targetId} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<AipRecordDto>.BadRequest(
                $"Cannot re-upload into a '{rec.Status}' record. Unlock it back to Draft first.");
        if (rec.EntrySource != "Upload")
            return ServiceResult<AipRecordDto>.BadRequest(
                "Only uploaded AIP records can be re-uploaded. This record was created through manual entry.");
        // A WFP built from this AIP holds FK-restricted references (aip_activity_id) into the
        // exact AipActivity rows the replace below would delete — the delete fails at the DB
        // constraint if we don't stop first, and even if it didn't, replacing the hierarchy
        // would orphan the WFP's line items against activity ids that no longer exist.
        if (await _wfpRepo.AnyForAipRecordAsync(targetId, ct))
            return ServiceResult<AipRecordDto>.BadRequest(
                "Cannot re-upload — a Work Financial Plan has already been built from this AIP. " +
                "Archive this record and upload the corrected file as a new AIP instead.");

        IReadOnlyList<AipOffice> existing = await _aipRepo.GetOfficesByAipIdAsync(targetId, ct);
        object old = new { rec.FiscalYear, rec.OriginalFilename, OfficeCount = existing.Count };
        foreach (AipOffice office in existing)
            await _officeRepo.DeleteAsync(office, ct);
        await _officeRepo.SaveChangesAsync(ct);

        rec.FiscalYear       = dto.FiscalYear;
        rec.OriginalFilename = dto.OriginalFilename;
        rec.UploadedAt       = DateTime.UtcNow;
        rec.Offices          = BuildOffices(dto.SectorOffices, fsDict, configOffices);

        await _aipRepo.UpdateAsync(rec, ct);
        await _aipRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("aip_records", rec.Id, AuditAction.Update,
            old,
            new { rec.FiscalYear, rec.OriginalFilename, OfficeCount = rec.Offices.Count },
            ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(rec));
    }

    /// <summary>
    /// ⚠️ <paramref name="offices"/> is what sets <see cref="AipOffice.OfficeId"/> (V18-32). Without
    /// it every uploaded office row lands unowned, and an unowned row is invisible to every scoped
    /// read — the office's own AIP would simply not appear, with no error anywhere.
    /// </summary>
    private static List<AipOffice> BuildOffices(
        Dictionary<string, List<ParsedAipOfficeDto>> sectorOffices,
        Dictionary<string, FundingSource> fsDict,
        IReadOnlyList<Office> offices) =>
        sectorOffices
            .SelectMany(kvp => kvp.Value)
            .Select(officeDto => new AipOffice
            {
                RefCode  = officeDto.RefCode,
                Name     = officeDto.Name,
                Sector   = officeDto.Sector,
                OfficeId = AipOfficeOwnership.ResolveOfficeId(officeDto.RefCode, offices),
                Programs = BuildPrograms(officeDto.Programs, fsDict),
            }).ToList();

    // ── Manual entry (RAL-62) — one node at a time ────────────────────────────

    public async Task<ServiceResult<AipRecordDto>> CreateManualRecordAsync(
        CreateAipRecordDto dto, Guid createdById, CancellationToken ct = default)
    {
        // ── Which shape? (V18-40) ─────────────────────────────────────────────
        // An office id means an office-owned record; its absence means the legacy multi-office
        // one. PPDO takes the same path as every other office — there is deliberately no branch
        // for it here, and adding one is what tracker B12-b ruled out.
        //
        // V18-37: the caller no longer gets a free choice — the fiscal year decides. Checked
        // BEFORE the office lookup on purpose: a caller asking for an office-owned FY2027 record
        // has made one mistake, the year, and answering "office 999 not found" (which may also be
        // true) would send them off to fix the wrong thing.
        if (AipShape.Mismatch(dto.FiscalYear, dto.OfficeConfigId) is string shapeError)
            return ServiceResult<AipRecordDto>.BadRequest(shapeError);

        Office? owningOffice = null;
        if (dto.OfficeConfigId is int officeConfigId)
        {
            owningOffice = await _officeConfigRepo.GetByIdAsync(officeConfigId, ct);
            if (owningOffice is null || !owningOffice.IsActive)
                return ServiceResult<AipRecordDto>.NotFound(
                    $"Office {officeConfigId} not found or inactive.");
        }

        // ⚠️ The conflict question differs by shape. For an office-owned record it must be scoped
        // to that office: asking "is there any AIP for FY 2028" would report the FIRST office's
        // record as a conflict for every other office in the province.
        AipRecord? conflict = owningOffice is not null
            ? await _aipRepo.GetByOfficeAndFiscalYearAsync(owningOffice.Id, dto.FiscalYear, ct)
            : await _aipRepo.GetLatestByFiscalYearAsync(dto.FiscalYear, ct);

        if (conflict is not null)
        {
            string hint = conflict.Status == PlanningStatus.Draft
                ? "Archive the existing record first before creating a new one."
                : "The existing record must be unlocked by an admin before a new one is allowed.";
            string scope = owningOffice is not null ? $"for {owningOffice.OfficeName} " : string.Empty;
            return ServiceResult<AipRecordDto>.BadRequest(
                $"An AIP {scope}for FY {dto.FiscalYear} already exists with status '{conflict.Status}'. {hint}");
        }

        AipRecord rec = new()
        {
            FiscalYear   = dto.FiscalYear,
            OfficeId     = owningOffice?.Id,
            EntrySource  = "Manual",
            UploadedById = createdById,
            UploadedAt   = DateTime.UtcNow,
            Status       = PlanningStatus.Draft,
        };
        await _aipRepo.AddAsync(rec, ct);
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_records", rec.Id, AuditAction.Create,
            null, new { rec.FiscalYear, rec.OfficeId, rec.EntrySource, rec.Status }, ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(rec));
    }

    public async Task<ServiceResult<AipOfficeDto>> AddOfficeAsync(
        int aipRecordId, CreateAipOfficeDto dto, User caller, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(aipRecordId, ct);
        if (rec is null)
            return ServiceResult<AipOfficeDto>.NotFound($"AIP record {aipRecordId} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"Cannot add to a '{rec.Status}' record. Unlock it back to Draft first.");

        if (!AipSector.Prefixes.TryGetValue(dto.Sector?.Trim() ?? string.Empty, out string? prefix))
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"Sector must be one of: {string.Join(", ", AipSector.Prefixes.Keys)}.");

        Office? office = await _officeConfigRepo.GetByIdAsync(dto.OfficeConfigId, ct);
        if (office is null || !office.IsActive)
            return ServiceResult<AipOfficeDto>.NotFound($"Office {dto.OfficeConfigId} not found or inactive.");

        // Ownership is decided by the office the caller ASKED to add, not by a node's parent —
        // this creates the node, so there is nothing to walk up from yet. Same NotFound as an
        // office that does not exist, for the reason on CheckWritableAsync.
        if (!OfficeScope.Resolve(caller).Permits(office.Id))
            return ServiceResult<AipOfficeDto>.NotFound($"Office {dto.OfficeConfigId} not found or inactive.");

        // ⚠️ V18-37 — the other door into a shape change, and the one no create-path gate can see.
        // Nothing here "converts" a record, but add two different offices to an office-owned record
        // and it spans several, which IS the legacy shape, reached a node at a time.
        //
        // The scope check above does not cover this: it stops a GUEST office reaching another
        // office's record and says nothing about the host-office admin, who legitimately sees every
        // office and would otherwise be free to do exactly this. BadRequest rather than the
        // NotFound used just above — that one hides existence; this caller may see the record and
        // is being told the operation is wrong for its shape.
        if (AipShape.RefuseForeignOffice(rec, office.Id, office.OfficeName) is string shapeError)
            return ServiceResult<AipOfficeDto>.BadRequest(shapeError);

        if (string.IsNullOrWhiteSpace(office.OfficeRefCode))
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"Office '{office.OfficeName}' has no AIP reference code configured. Set it in Office Config first.");

        string sector  = dto.Sector!.Trim().ToUpperInvariant();
        string refCode = $"{prefix}-000-1-{office.OfficeRefCode}";
        string name    = string.IsNullOrWhiteSpace(dto.Name) ? office.OfficeName : dto.Name.Trim();

        // Same RefCode CAN legitimately repeat for the same office (real AIP files list several
        // sub-office/program-cluster rows under one physical office, e.g. "OFFICE OF THE GOVERNOR
        // - WARDEN" and "OFFICE OF THE GOVERNOR - AKAP-HUB" both under ref code 3000-000-1-01-001)
        // — so only reject a RefCode+Name pair that's an exact repeat (an accidental double-add),
        // not every repeat of the RefCode alone.
        IReadOnlyList<AipOffice> siblings = await _aipRepo.GetOfficesByAipIdAsync(aipRecordId, ct);
        if (siblings.Any(o => o.RefCode == refCode && o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"'{name}' is already added to this AIP under {sector}.");

        AipOffice entity = new()
        {
            AipRecordId = aipRecordId,
            RefCode     = refCode,
            Name        = name,
            Sector      = sector,
        };
        await _officeRepo.AddAsync(entity, ct);
        await _officeRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_offices", entity.Id, AuditAction.Create,
            null, new { entity.AipRecordId, entity.RefCode, entity.Name, entity.Sector }, ct);

        return ServiceResult<AipOfficeDto>.Ok(
            new AipOfficeDto(entity.Id, entity.AipRecordId, entity.RefCode, entity.Name, entity.Sector,
                Array.Empty<AipProgramDto>()));
    }

    public async Task<ServiceResult<AipOfficeDto>> CopyOfficeFromPriorYearAsync(
        CopyAipOfficeDto dto, Guid createdById, User caller, CancellationToken ct = default)
    {
        // ⚠️ V18-37, and this one was a live leak rather than a hypothetical. The find-or-create
        // below builds its target record with no owner set, so before this guard, carrying forward
        // into FY2028 wrote a legacy-shape record into a year that must not have one — silently,
        // with nothing downstream positioned to notice. Carry-forward into the new shape is Phase 3
        // work; until it exists the refusal is the honest answer.
        //
        // First statement in the method, deliberately: a refusal that arrives after the record has
        // been added leaves the wrong-shaped row behind and merely reports an error about it, which
        // is the original bug with a message attached.
        if (AipShape.Mismatch(dto.TargetFiscalYear, officeId: null) is string shapeError)
            return ServiceResult<AipOfficeDto>.BadRequest(shapeError);

        if (dto.ProgramIds is null || dto.ProgramIds.Count == 0)
            return ServiceResult<AipOfficeDto>.BadRequest("Select at least one program to copy.");

        AipOffice? sourceOffice = await _aipRepo.GetOfficeByIdAsync(dto.SourceOfficeId, ct);
        if (sourceOffice is null)
            return ServiceResult<AipOfficeDto>.NotFound($"AIP office {dto.SourceOfficeId} not found.");

        // Every requested program must actually belong to the source office.
        IReadOnlyList<AipProgram> sourcePrograms =
            await _aipRepo.GetProgramsByOfficeIdsAsync([dto.SourceOfficeId], ct);
        Dictionary<int, AipProgram> sourceProgramsById = sourcePrograms.ToDictionary(p => p.Id);
        List<int> unknownIds = dto.ProgramIds.Where(id => !sourceProgramsById.ContainsKey(id)).ToList();
        if (unknownIds.Count > 0)
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"Program id(s) {string.Join(", ", unknownIds)} do not belong to office {dto.SourceOfficeId}.");

        List<AipProgram> programsToCopy = dto.ProgramIds.Select(id => sourceProgramsById[id]).ToList();

        // Find-or-create the target AipRecord. Unlike CreateManualRecordAsync (which rejects
        // outright if any active record exists), carry-forward specifically targets an existing
        // Draft Manual record if one is already there — only creates fresh when none exists.
        AipRecord? targetRecord = await _aipRepo.GetLatestByFiscalYearAsync(dto.TargetFiscalYear, ct);
        bool creatingRecord = targetRecord is null;
        if (targetRecord is not null
            && (targetRecord.EntrySource != "Manual" || targetRecord.Status != PlanningStatus.Draft))
        {
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"An AIP for FY {dto.TargetFiscalYear} already exists (entry source " +
                $"'{targetRecord.EntrySource}', status '{targetRecord.Status}'). Carry-forward " +
                "requires a Draft Manual-entry record for the target year.");
        }

        if (creatingRecord)
        {
            targetRecord = new AipRecord
            {
                FiscalYear   = dto.TargetFiscalYear,
                EntrySource  = "Manual",
                UploadedById = createdById,
                UploadedAt   = DateTime.UtcNow,
                Status       = PlanningStatus.Draft,
            };
            await _aipRepo.AddAsync(targetRecord, ct);
            await _aipRepo.SaveChangesAsync(ct);
        }

        // Find-or-create the target AipOffice — same RefCode as the source office. RefCode is
        // year-independent (derived from sector prefix + the config Office's OfficeRefCode), so
        // reusing it verbatim is correct, not a coincidence.
        IReadOnlyList<AipOffice> targetOffices = await _aipRepo.GetOfficesByAipIdAsync(targetRecord!.Id, ct);
        AipOffice? targetOffice = targetOffices.FirstOrDefault(o => o.RefCode == sourceOffice.RefCode);
        bool creatingOffice = targetOffice is null;

        // Collision guard — reject the whole request if any selected program's RefCode already
        // exists under the target office (already carried forward once). Never silently skip.
        if (!creatingOffice)
        {
            IReadOnlyList<AipProgram> existingTargetPrograms =
                await _aipRepo.GetProgramsByOfficeIdsAsync([targetOffice!.Id], ct);
            List<string> collisions = programsToCopy
                .Select(p => p.RefCode)
                .Intersect(existingTargetPrograms.Select(p => p.RefCode))
                .ToList();
            if (collisions.Count > 0)
                return ServiceResult<AipOfficeDto>.BadRequest(
                    "The following program ref codes are already copied into this office: " +
                    string.Join(", ", collisions) + ".");
        }

        // Load the full subtree under the selected programs.
        List<int> programIdsToCopy = programsToCopy.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject> sourceProjects =
            await _aipRepo.GetProjectsByProgramIdsAsync(programIdsToCopy, ct);
        List<int> sourceProjectIds = sourceProjects.Select(j => j.Id).ToList();
        IReadOnlyList<AipActivity> sourceActivities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(sourceProjectIds, ct);

        // Clone Program -> Projects -> Activities with fresh identity throughout. IsCreation
        // resets to false — it's captured during WFP data entry, not an AIP-import-time fact,
        // so it must not silently carry over from last year's WFP decisions (RAL-180 acceptance
        // criteria). IsSynthetic is copied as-is since it reflects structural shape from the
        // original import, not a WFP decision.
        List<AipProgram> clonedPrograms = programsToCopy.Select(p => new AipProgram
        {
            RefCode      = p.RefCode,
            Name         = p.Name,
            FunctionBand = p.FunctionBand,
            Projects = sourceProjects.Where(j => j.ProgramId == p.Id).Select(j => new AipProject
            {
                RefCode     = j.RefCode,
                Name        = j.Name,
                IsSynthetic = j.IsSynthetic,
                Activities = sourceActivities.Where(a => a.ProjectId == j.Id).Select(a => new AipActivity
                {
                    RefCode               = a.RefCode,
                    Name                  = a.Name,
                    EsreCode              = a.EsreCode,
                    ImplementingOffice    = a.ImplementingOffice,
                    StartDate             = a.StartDate,
                    EndDate               = a.EndDate,
                    ExpectedOutputs       = a.ExpectedOutputs,
                    FundingSourceId       = a.FundingSourceId,
                    FundingSourceSnapshot = a.FundingSourceSnapshot,
                    Ps                    = a.Ps,
                    Mooe                  = a.Mooe,
                    Co                    = a.Co,
                    Total                 = a.Total,
                    CcAdaptation          = a.CcAdaptation,
                    CcMitigation          = a.CcMitigation,
                    CcTypologyCode        = a.CcTypologyCode,
                    IsCreation            = false,
                    IsSynthetic           = a.IsSynthetic,
                }).ToList(),
            }).ToList(),
        }).ToList();

        if (creatingOffice)
        {
            targetOffice = new AipOffice
            {
                AipRecordId = targetRecord.Id,
                RefCode     = sourceOffice.RefCode,
                Name        = sourceOffice.Name,
                Sector      = sourceOffice.Sector,
                Programs    = clonedPrograms,
            };
            await _officeRepo.AddAsync(targetOffice, ct);
            await _officeRepo.SaveChangesAsync(ct);
        }
        else
        {
            // Office already exists — queue each cloned program (with its nested subtree) and
            // flush them together in one SaveChangesAsync, so an N-program copy is one transaction.
            foreach (AipProgram program in clonedPrograms)
            {
                program.OfficeId = targetOffice!.Id;
                await _programRepo.AddAsync(program, ct);
            }
            await _programRepo.SaveChangesAsync(ct);
        }

        await _audit.LogAsync("aip_offices", targetOffice!.Id, AuditAction.Create, null, new
        {
            targetOffice.AipRecordId,
            targetOffice.RefCode,
            targetOffice.Name,
            SourceAipOfficeId = dto.SourceOfficeId,
            SourceAipRecordId = sourceOffice.AipRecordId,
            CopiedProgramIds  = dto.ProgramIds,
        }, ct);

        // Build the response from the target office's COMPLETE current program list — not just
        // clonedPrograms. The frontend replaces the whole office node in its tree with this
        // response; if the office already existed and the response only carried the newly-added
        // slice, its pre-existing programs would silently vanish from the UI. A brand-new office
        // has no pre-existing programs, so clonedPrograms (already fully populated in memory) is
        // already the complete list there — no need to round-trip through the repository.
        IReadOnlyList<AipProgramDto> programDtos;
        if (creatingOffice)
        {
            programDtos = clonedPrograms.Select(p => new AipProgramDto(
                p.Id, targetOffice.Id, p.RefCode, p.Name,
                p.Projects.Select(j => new AipProjectDto(
                    j.Id, p.Id, j.RefCode, j.Name,
                    j.Activities.Select(MapActivityToDto).ToList(),
                    j.IsSynthetic)).ToList(),
                p.FunctionBand)).ToList();
        }
        else
        {
            IReadOnlyList<AipProgram> allTargetPrograms =
                await _aipRepo.GetProgramsByOfficeIdsAsync([targetOffice.Id], ct);
            List<int> allTargetProgramIds = allTargetPrograms.Select(p => p.Id).ToList();
            IReadOnlyList<AipProject> allTargetProjects =
                await _aipRepo.GetProjectsByProgramIdsAsync(allTargetProgramIds, ct);
            List<int> allTargetProjectIds = allTargetProjects.Select(j => j.Id).ToList();
            IReadOnlyList<AipActivity> allTargetActivities =
                await _aipRepo.GetActivitiesByProjectIdsAsync(allTargetProjectIds, ct);

            programDtos = allTargetPrograms.Select(p => new AipProgramDto(
                p.Id, targetOffice.Id, p.RefCode, p.Name,
                allTargetProjects.Where(j => j.ProgramId == p.Id).Select(j => new AipProjectDto(
                    j.Id, p.Id, j.RefCode, j.Name,
                    allTargetActivities.Where(a => a.ProjectId == j.Id).Select(MapActivityToDto).ToList(),
                    j.IsSynthetic)).ToList(),
                p.FunctionBand)).ToList();
        }

        return ServiceResult<AipOfficeDto>.Ok(
            new AipOfficeDto(targetOffice.Id, targetOffice.AipRecordId, targetOffice.RefCode,
                targetOffice.Name, targetOffice.Sector, programDtos));
    }

    public async Task<ServiceResult<AipOfficeDto>> SeedProgramsFromLdipAsync(
        SeedAipProgramsFromLdipDto dto, Guid createdById, User caller, CancellationToken ct = default)
    {
        // ⚠️ V18-37 — the same live leak as CopyOfficeFromPriorYearAsync, for the same reason: the
        // find-or-create below constructs an unowned record. Note that the DTO already names an
        // office, which makes this look like it could simply own the record it creates — it cannot,
        // because that office is the LDIP SOURCE being read from, not a decision about who owns the
        // target. Conflating the two is how this path would acquire the new shape by accident.
        if (AipShape.Mismatch(dto.TargetFiscalYear, officeId: null) is string shapeError)
            return ServiceResult<AipOfficeDto>.BadRequest(shapeError);

        if (dto.LdipProgramIds is null || dto.LdipProgramIds.Count == 0)
            return ServiceResult<AipOfficeDto>.BadRequest("Select at least one program to seed.");

        if (!AipSector.Prefixes.TryGetValue(dto.Sector?.Trim() ?? string.Empty, out string? prefix))
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"Sector must be one of: {string.Join(", ", AipSector.Prefixes.Keys)}.");
        string sector = dto.Sector!.Trim().ToUpperInvariant();

        Office? office = await _officeConfigRepo.GetByIdAsync(dto.OfficeConfigId, ct);
        if (office is null || !office.IsActive)
            return ServiceResult<AipOfficeDto>.NotFound($"Office {dto.OfficeConfigId} not found or inactive.");

        // Resolve the matching LdipOffice in two tiers:
        //
        // Tier 1 — this office's own LDIP records (LdipRecord.OfficeId = this office; entry modes
        // New/Amendment/Supplemental). Scan non-Archived records newest-first for the first sector
        // group match. Sector text match is safe here because GetOfficeGroupsAsync for one of these
        // records only ever returns groups that already belong to this one office.
        //
        // Tier 2 — multi-office Upload records (LdipRecord.OfficeId is null — one document spans
        // every office, RAL-165/LdipService.ConfirmImportAsync). These never surface via Tier 1's
        // office-scoped query at all, so an office with no Tier-1 record of its own (e.g. its only
        // dedicated LDIP was archived) would otherwise never find real historical LDIP data even
        // though the bulk-uploaded document contains it. Sector text alone isn't enough to pick the
        // right group out of a multi-office document (many offices share "General"), so match by
        // this office's own computed AIP ref code instead — the same unambiguous identity every
        // AipOffice/LdipOffice RefCode already carries.
        LdipOffice? sourceGroup = null;
        IReadOnlyList<LdipRecord> ownRecords = await _ldipRepo.GetListAsync(dto.OfficeConfigId, null, ct);
        foreach (LdipRecord ldipRecord in ownRecords.Where(r => r.Status != PlanningStatus.Archived))
        {
            IReadOnlyList<LdipOffice> groups = await _ldipRepo.GetOfficeGroupsAsync(ldipRecord.Id, ct);
            sourceGroup = groups.FirstOrDefault(g => g.Sector.Equals(dto.Sector, StringComparison.OrdinalIgnoreCase));
            if (sourceGroup is not null) break;
        }

        if (sourceGroup is null && !string.IsNullOrWhiteSpace(office.OfficeRefCode))
        {
            string expectedRefCode = $"{prefix}-000-1-{office.OfficeRefCode}";
            IReadOnlyList<LdipRecord> allRecords = await _ldipRepo.GetListAsync(null, null, ct);
            IEnumerable<LdipRecord> multiOfficeRecords = allRecords
                .Where(r => r.OfficeId is null && r.Status != PlanningStatus.Archived);
            foreach (LdipRecord ldipRecord in multiOfficeRecords)
            {
                IReadOnlyList<LdipOffice> groups = await _ldipRepo.GetOfficeGroupsAsync(ldipRecord.Id, ct);
                sourceGroup = groups.FirstOrDefault(
                    g => g.RefCode.Equals(expectedRefCode, StringComparison.OrdinalIgnoreCase));
                if (sourceGroup is not null) break;
            }
        }

        if (sourceGroup is null)
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"'{office.OfficeName}' has no LDIP for the {sector} sector.");

        // Every requested LDIP program must actually belong to the resolved group.
        Dictionary<int, LdipProgram> sourceProgramsById = sourceGroup.Programs.ToDictionary(p => p.Id);
        List<int> unknownIds = dto.LdipProgramIds.Where(id => !sourceProgramsById.ContainsKey(id)).ToList();
        if (unknownIds.Count > 0)
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"LDIP program id(s) {string.Join(", ", unknownIds)} do not belong to this office's " +
                $"{sector} LDIP.");

        List<LdipProgram> programsToSeed = dto.LdipProgramIds.Select(id => sourceProgramsById[id]).ToList();

        // Find-or-create the target AipRecord — identical rule to CopyOfficeFromPriorYearAsync.
        AipRecord? targetRecord = await _aipRepo.GetLatestByFiscalYearAsync(dto.TargetFiscalYear, ct);
        bool creatingRecord = targetRecord is null;
        if (targetRecord is not null
            && (targetRecord.EntrySource != "Manual" || targetRecord.Status != PlanningStatus.Draft))
        {
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"An AIP for FY {dto.TargetFiscalYear} already exists (entry source " +
                $"'{targetRecord.EntrySource}', status '{targetRecord.Status}'). Seeding from LDIP " +
                "requires a Draft Manual-entry record for the target year.");
        }

        if (creatingRecord)
        {
            targetRecord = new AipRecord
            {
                FiscalYear   = dto.TargetFiscalYear,
                EntrySource  = "Manual",
                UploadedById = createdById,
                UploadedAt   = DateTime.UtcNow,
                Status       = PlanningStatus.Draft,
            };
            await _aipRepo.AddAsync(targetRecord, ct);
            await _aipRepo.SaveChangesAsync(ct);
        }

        // Find-or-create the target AipOffice — same RefCode as the LdipOffice group (year-
        // independent, derived the same way on both sides: sector prefix + office ref code).
        IReadOnlyList<AipOffice> targetOffices = await _aipRepo.GetOfficesByAipIdAsync(targetRecord!.Id, ct);
        AipOffice? targetOffice = targetOffices.FirstOrDefault(o => o.RefCode == sourceGroup.RefCode);
        bool creatingOffice = targetOffice is null;

        // Collision guard — reject the whole request if any selected program's RefCode already
        // exists under the target office. Never silently skip/overwrite.
        if (!creatingOffice)
        {
            IReadOnlyList<AipProgram> existingTargetPrograms =
                await _aipRepo.GetProgramsByOfficeIdsAsync([targetOffice!.Id], ct);
            List<string> collisions = programsToSeed
                .Select(p => p.RefCode)
                .Intersect(existingTargetPrograms.Select(p => p.RefCode))
                .ToList();
            if (collisions.Count > 0)
                return ServiceResult<AipOfficeDto>.BadRequest(
                    "The following program ref codes already exist under this office: " +
                    string.Join(", ", collisions) + ".");
        }

        // Bare-shell AipProgram rows — Name+RefCode only, FunctionBand defaults to Core (LDIP has
        // no equivalent field). No Project/Activity rows; no LDIP budget/funding-source/schedule/
        // CC/alignment fields are copied — LdipProgram.Budget is a multi-year total, not a valid
        // single-fiscal-year figure (see the ticket's "why amounts don't carry over" reasoning).
        List<AipProgram> seededPrograms = programsToSeed.Select(p => new AipProgram
        {
            RefCode      = p.RefCode,
            Name         = p.Name,
            FunctionBand = AipFunctionBand.Core,
        }).ToList();

        if (creatingOffice)
        {
            targetOffice = new AipOffice
            {
                AipRecordId = targetRecord.Id,
                RefCode     = sourceGroup.RefCode,
                Name        = sourceGroup.Name,
                Sector      = sector,
                Programs    = seededPrograms,
            };
            await _officeRepo.AddAsync(targetOffice, ct);
            await _officeRepo.SaveChangesAsync(ct);
        }
        else
        {
            foreach (AipProgram program in seededPrograms)
            {
                program.OfficeId = targetOffice!.Id;
                await _programRepo.AddAsync(program, ct);
            }
            await _programRepo.SaveChangesAsync(ct);
        }

        await _audit.LogAsync("aip_offices", targetOffice!.Id, AuditAction.Create, null, new
        {
            targetOffice.AipRecordId,
            targetOffice.RefCode,
            targetOffice.Name,
            SourceLdipOfficeId   = sourceGroup.Id,
            SeededLdipProgramIds = dto.LdipProgramIds,
        }, ct);

        // Same completeness-safe response construction as CopyOfficeFromPriorYearAsync — rebuild
        // from the target office's complete current program list so pre-existing programs/
        // projects/activities are never dropped from the response.
        IReadOnlyList<AipProgramDto> programDtos;
        if (creatingOffice)
        {
            programDtos = seededPrograms.Select(p => new AipProgramDto(
                p.Id, targetOffice.Id, p.RefCode, p.Name, Array.Empty<AipProjectDto>(), p.FunctionBand)).ToList();
        }
        else
        {
            IReadOnlyList<AipProgram> allTargetPrograms =
                await _aipRepo.GetProgramsByOfficeIdsAsync([targetOffice.Id], ct);
            List<int> allTargetProgramIds = allTargetPrograms.Select(p => p.Id).ToList();
            IReadOnlyList<AipProject> allTargetProjects =
                await _aipRepo.GetProjectsByProgramIdsAsync(allTargetProgramIds, ct);
            List<int> allTargetProjectIds = allTargetProjects.Select(j => j.Id).ToList();
            IReadOnlyList<AipActivity> allTargetActivities =
                await _aipRepo.GetActivitiesByProjectIdsAsync(allTargetProjectIds, ct);

            programDtos = allTargetPrograms.Select(p => new AipProgramDto(
                p.Id, targetOffice.Id, p.RefCode, p.Name,
                allTargetProjects.Where(j => j.ProgramId == p.Id).Select(j => new AipProjectDto(
                    j.Id, p.Id, j.RefCode, j.Name,
                    allTargetActivities.Where(a => a.ProjectId == j.Id).Select(MapActivityToDto).ToList(),
                    j.IsSynthetic)).ToList(),
                p.FunctionBand)).ToList();
        }

        return ServiceResult<AipOfficeDto>.Ok(
            new AipOfficeDto(targetOffice.Id, targetOffice.AipRecordId, targetOffice.RefCode,
                targetOffice.Name, targetOffice.Sector, programDtos));
    }

    public async Task<ServiceResult<AipProgramDto>> AddProgramAsync(
        int officeId, CreateAipProgramDto dto, User caller, CancellationToken ct = default)
    {
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(officeId, ct);
        if (office is null)
            return ServiceResult<AipProgramDto>.NotFound($"AIP office {officeId} not found.");

        ServiceResult<AipProgramDto>? statusError = await CheckWritableAsync<AipProgramDto>(office, caller, $"AIP office {officeId} not found.", ct);
        if (statusError is not null) return statusError;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<AipProgramDto>.BadRequest("Program name is required.");

        string functionBand;
        if (string.IsNullOrWhiteSpace(dto.FunctionBand))
        {
            functionBand = AipFunctionBand.Core; // new programs default to Core, same as import
        }
        else if (!TryCanonicalizeFunctionBand(dto.FunctionBand, out string? canonical, out string? error))
        {
            return ServiceResult<AipProgramDto>.BadRequest(error!);
        }
        else
        {
            functionBand = canonical!;
        }

        IReadOnlyList<AipProgram> siblings = await _aipRepo.GetProgramsByOfficeIdsAsync([officeId], ct);
        string refCode = NextRefCode(office.RefCode, siblings.Select(p => p.RefCode));

        AipProgram entity = new()
        {
            OfficeId     = officeId,
            RefCode      = refCode,
            Name         = dto.Name.Trim(),
            FunctionBand = functionBand,
        };
        await _programRepo.AddAsync(entity, ct);
        await _programRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_programs", entity.Id, AuditAction.Create,
            null, new { entity.OfficeId, entity.RefCode, entity.Name, entity.FunctionBand }, ct);

        return ServiceResult<AipProgramDto>.Ok(
            new AipProgramDto(entity.Id, entity.OfficeId, entity.RefCode, entity.Name,
                Array.Empty<AipProjectDto>(), entity.FunctionBand));
    }

    public async Task<ServiceResult<AipProjectDto>> AddProjectAsync(
        int programId, CreateAipProjectDto dto, User caller, CancellationToken ct = default)
    {
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(programId, ct);
        if (program is null)
            return ServiceResult<AipProjectDto>.NotFound($"AIP program {programId} not found.");

        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<AipProjectDto>.NotFound($"AIP office {program.OfficeId} not found.");

        ServiceResult<AipProjectDto>? statusError = await CheckWritableAsync<AipProjectDto>(office, caller, $"AIP program {programId} not found.", ct);
        if (statusError is not null) return statusError;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<AipProjectDto>.BadRequest("Project name is required.");

        IReadOnlyList<AipProject> siblings = await _aipRepo.GetProjectsByProgramIdsAsync([programId], ct);
        string refCode = NextRefCode(program.RefCode, siblings.Select(j => j.RefCode));

        AipProject entity = new() { ProgramId = programId, RefCode = refCode, Name = dto.Name.Trim() };
        await _projectRepo.AddAsync(entity, ct);
        await _projectRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_projects", entity.Id, AuditAction.Create,
            null, new { entity.ProgramId, entity.RefCode, entity.Name }, ct);

        return ServiceResult<AipProjectDto>.Ok(
            new AipProjectDto(entity.Id, entity.ProgramId, entity.RefCode, entity.Name,
                Array.Empty<AipActivityDto>()));
    }

    public async Task<ServiceResult<AipActivityDto>> AddActivityAsync(
        int projectId, CreateAipActivityDto dto, User caller, CancellationToken ct = default)
    {
        AipProject? project = await _aipRepo.GetProjectByIdAsync(projectId, ct);
        if (project is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP project {projectId} not found.");

        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP program {project.ProgramId} not found.");

        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP office {program.OfficeId} not found.");

        ServiceResult<AipActivityDto>? statusError = await CheckWritableAsync<AipActivityDto>(office, caller, $"AIP project {projectId} not found.", ct);
        if (statusError is not null) return statusError;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<AipActivityDto>.BadRequest("Activity name is required.");
        if (!string.IsNullOrWhiteSpace(dto.EsreCode) && !AipEsreCode.AllowedValues.Contains(dto.EsreCode.Trim().ToUpperInvariant()))
            return ServiceResult<AipActivityDto>.BadRequest(
                $"eSRE code must be one of: {string.Join(", ", AipEsreCode.AllowedValues)}.");

        IReadOnlyList<FundingSource> fsList = await _fsRepo.GetAllAsync(ct);
        FundingSource? fs = string.IsNullOrWhiteSpace(dto.FundingSourceRaw)
            ? null
            : fsList.FirstOrDefault(f => f.Code.Equals(dto.FundingSourceRaw, StringComparison.OrdinalIgnoreCase));

        IReadOnlyList<AipActivity> siblings = await _aipRepo.GetActivitiesByProjectIdsAsync([projectId], ct);
        string refCode = NextRefCode(project.RefCode, siblings.Select(a => a.RefCode));

        decimal? total = dto.Ps is null && dto.Mooe is null && dto.Co is null
            ? null
            : (dto.Ps ?? 0) + (dto.Mooe ?? 0) + (dto.Co ?? 0);

        AipActivity entity = new()
        {
            ProjectId             = projectId,
            RefCode               = refCode,
            Name                  = dto.Name.Trim(),
            EsreCode              = string.IsNullOrWhiteSpace(dto.EsreCode) ? null : dto.EsreCode.Trim().ToUpperInvariant(),
            ImplementingOffice    = dto.ImplementingOffice,
            StartDate             = dto.StartDate,
            EndDate               = dto.EndDate,
            ExpectedOutputs       = dto.ExpectedOutputs,
            FundingSourceId       = fs?.Id,
            FundingSourceSnapshot = fs?.Code ?? dto.FundingSourceRaw,
            Ps                    = dto.Ps,
            Mooe                  = dto.Mooe,
            Co                    = dto.Co,
            Total                 = total,
            CcAdaptation          = dto.CcAdaptation,
            CcMitigation          = dto.CcMitigation,
            CcTypologyCode        = dto.CcTypologyCode,
        };
        await _activityRepo.AddAsync(entity, ct);
        await _activityRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_activities", entity.Id, AuditAction.Create,
            null, new { entity.ProjectId, entity.RefCode, entity.Name, entity.Total }, ct);

        return ServiceResult<AipActivityDto>.Ok(MapActivityToDto(entity));
    }

    // ── Inline office/program/project edit (detail-page CRUD follow-up to RAL-179) ──

    public async Task<ServiceResult<AipOfficeDto>> UpdateOfficeAsync(
        int officeId, UpdateAipOfficeDto dto, User caller, CancellationToken ct = default)
    {
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(officeId, ct);
        if (office is null)
            return ServiceResult<AipOfficeDto>.NotFound($"AIP office {officeId} not found.");

        ServiceResult<AipOfficeDto>? statusError = await CheckWritableAsync<AipOfficeDto>(office, caller, $"AIP office {officeId} not found.", ct, "edit");
        if (statusError is not null) return statusError;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<AipOfficeDto>.BadRequest("Office name is required.");

        string newName = dto.Name.Trim();

        // Same RefCode+Name collision guard as AddOfficeAsync — a rename can't land on another
        // sibling's exact (RefCode, Name), but same-RefCode-different-name (sub-offices) is fine.
        IReadOnlyList<AipOffice> siblings = await _aipRepo.GetOfficesByAipIdAsync(office.AipRecordId, ct);
        if (siblings.Any(o => o.Id != officeId && o.RefCode == office.RefCode
            && o.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<AipOfficeDto>.BadRequest(
                $"'{newName}' already exists under this AIP for that ref code.");

        string oldName = office.Name;
        office.Name = newName;
        await _officeRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_offices", office.Id, AuditAction.Update,
            new { Name = oldName }, new { office.Name }, ct);

        return ServiceResult<AipOfficeDto>.Ok(
            new AipOfficeDto(office.Id, office.AipRecordId, office.RefCode, office.Name, office.Sector,
                Array.Empty<AipProgramDto>()));
    }

    public async Task<ServiceResult<AipProgramDto>> UpdateProgramAsync(
        int programId, UpdateAipProgramDto dto, User caller, CancellationToken ct = default)
    {
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(programId, ct);
        if (program is null)
            return ServiceResult<AipProgramDto>.NotFound($"AIP program {programId} not found.");

        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<AipProgramDto>.NotFound($"AIP office {program.OfficeId} not found.");

        ServiceResult<AipProgramDto>? statusError = await CheckWritableAsync<AipProgramDto>(office, caller, $"AIP program {programId} not found.", ct, "edit");
        if (statusError is not null) return statusError;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<AipProgramDto>.BadRequest("Program name is required.");

        string functionBand;
        if (string.IsNullOrWhiteSpace(dto.FunctionBand))
        {
            functionBand = program.FunctionBand ?? AipFunctionBand.Core;
        }
        else if (!TryCanonicalizeFunctionBand(dto.FunctionBand, out string? canonical, out string? error))
        {
            return ServiceResult<AipProgramDto>.BadRequest(error!);
        }
        else
        {
            functionBand = canonical!;
        }

        object old = new { program.Name, program.FunctionBand };
        program.Name         = dto.Name.Trim();
        program.FunctionBand = functionBand;
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_programs", program.Id, AuditAction.Update,
            old, new { program.Name, program.FunctionBand }, ct);

        // Field-update response — Projects intentionally omitted, same convention as
        // UpdateProgramFunctionBandAsync (callers patch their own local state by field).
        return ServiceResult<AipProgramDto>.Ok(new AipProgramDto(
            program.Id, program.OfficeId, program.RefCode, program.Name,
            Array.Empty<AipProjectDto>(), program.FunctionBand));
    }

    public async Task<ServiceResult<AipProjectDto>> UpdateProjectAsync(
        int projectId, UpdateAipProjectDto dto, User caller, CancellationToken ct = default)
    {
        AipProject? project = await _aipRepo.GetProjectByIdAsync(projectId, ct);
        if (project is null)
            return ServiceResult<AipProjectDto>.NotFound($"AIP project {projectId} not found.");

        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null)
            return ServiceResult<AipProjectDto>.NotFound($"AIP program {project.ProgramId} not found.");
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<AipProjectDto>.NotFound($"AIP office {program.OfficeId} not found.");

        ServiceResult<AipProjectDto>? statusError = await CheckWritableAsync<AipProjectDto>(office, caller, $"AIP project {projectId} not found.", ct, "edit");
        if (statusError is not null) return statusError;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<AipProjectDto>.BadRequest("Project name is required.");

        string oldName = project.Name;
        project.Name = dto.Name.Trim();
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_projects", project.Id, AuditAction.Update,
            new { Name = oldName }, new { project.Name }, ct);

        return ServiceResult<AipProjectDto>.Ok(
            new AipProjectDto(project.Id, project.ProgramId, project.RefCode, project.Name,
                Array.Empty<AipActivityDto>(), project.IsSynthetic));
    }

    // ── Inline activity edit (RAL-179) ────────────────────────────────────────

    public async Task<ServiceResult<AipActivityDto>> UpdateActivityAsync(
        int aipRecordId, int activityId, UpdateAipActivityDto dto, User caller, CancellationToken ct = default)
    {
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(activityId, ct);
        if (activity is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP activity {activityId} not found.");

        AipProject? project = await _aipRepo.GetProjectByIdAsync(activity.ProjectId, ct);
        if (project is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP project {activity.ProjectId} not found.");
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP program {project.ProgramId} not found.");
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP office {program.OfficeId} not found.");
        if (office.AipRecordId != aipRecordId)
            return ServiceResult<AipActivityDto>.NotFound(
                $"AIP activity {activityId} does not belong to AIP record {aipRecordId}.");

        ServiceResult<AipActivityDto>? statusError = await CheckWritableAsync<AipActivityDto>(office, caller, $"AIP activity {activityId} not found.", ct, "edit");
        if (statusError is not null) return statusError;

        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<AipActivityDto>.BadRequest("Activity name is required.");
        if (!string.IsNullOrWhiteSpace(dto.EsreCode) && !AipEsreCode.AllowedValues.Contains(dto.EsreCode.Trim().ToUpperInvariant()))
            return ServiceResult<AipActivityDto>.BadRequest(
                $"eSRE code must be one of: {string.Join(", ", AipEsreCode.AllowedValues)}.");

        FundingSource? fs = null;
        if (dto.FundingSourceId is int fsId)
        {
            IReadOnlyList<FundingSource> fsList = await _fsRepo.GetAllAsync(ct);
            fs = fsList.FirstOrDefault(f => f.Id == fsId);
            if (fs is null)
                return ServiceResult<AipActivityDto>.BadRequest($"Funding source {fsId} not found.");
        }

        decimal? total = dto.Ps is null && dto.Mooe is null && dto.Co is null
            ? null
            : (dto.Ps ?? 0) + (dto.Mooe ?? 0) + (dto.Co ?? 0);

        object old = new
        {
            activity.Name, activity.EsreCode, activity.ImplementingOffice, activity.StartDate, activity.EndDate,
            activity.ExpectedOutputs, activity.FundingSourceId, activity.FundingSourceSnapshot,
            activity.Ps, activity.Mooe, activity.Co, activity.Total,
            activity.CcAdaptation, activity.CcMitigation, activity.CcTypologyCode,
        };

        activity.Name                  = dto.Name.Trim();
        activity.EsreCode              = string.IsNullOrWhiteSpace(dto.EsreCode) ? null : dto.EsreCode.Trim().ToUpperInvariant();
        activity.ImplementingOffice    = dto.ImplementingOffice;
        activity.StartDate             = dto.StartDate;
        activity.EndDate               = dto.EndDate;
        activity.ExpectedOutputs       = dto.ExpectedOutputs;
        activity.FundingSourceId       = fs?.Id;
        activity.FundingSourceSnapshot = fs?.Code;
        activity.Ps                    = dto.Ps;
        activity.Mooe                  = dto.Mooe;
        activity.Co                    = dto.Co;
        activity.Total                 = total;
        activity.CcAdaptation          = dto.CcAdaptation;
        activity.CcMitigation          = dto.CcMitigation;
        activity.CcTypologyCode        = dto.CcTypologyCode;

        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_activities", activity.Id, AuditAction.Update, old,
            new
            {
                activity.Name, activity.EsreCode, activity.ImplementingOffice, activity.StartDate, activity.EndDate,
                activity.ExpectedOutputs, activity.FundingSourceId, activity.FundingSourceSnapshot,
                activity.Ps, activity.Mooe, activity.Co, activity.Total,
                activity.CcAdaptation, activity.CcMitigation, activity.CcTypologyCode,
            }, ct);

        return ServiceResult<AipActivityDto>.Ok(MapActivityToDto(activity));
    }

    // ── Delete (mistakes happen — mirrors the Add* guard chain) ───────────────

    public async Task<ServiceResult<bool>> DeleteOfficeAsync(int officeId, User caller, CancellationToken ct = default)
    {
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(officeId, ct);
        if (office is null)
            return ServiceResult<bool>.NotFound($"AIP office {officeId} not found.");

        ServiceResult<bool>? statusError = await CheckWritableAsync<bool>(office, caller, $"AIP office {officeId} not found.", ct, "delete from");
        if (statusError is not null) return statusError;

        // DB cascade (AipOffice -> AipProgram -> AipProject -> AipActivity) removes the whole subtree.
        await _officeRepo.DeleteAsync(office, ct);
        await _officeRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_offices", office.Id, AuditAction.Delete,
            new { office.AipRecordId, office.RefCode, office.Name, office.Sector }, null, ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> DeleteProgramAsync(int programId, User caller, CancellationToken ct = default)
    {
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(programId, ct);
        if (program is null)
            return ServiceResult<bool>.NotFound($"AIP program {programId} not found.");

        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<bool>.NotFound($"AIP office {program.OfficeId} not found.");

        ServiceResult<bool>? statusError = await CheckWritableAsync<bool>(office, caller, $"AIP program {programId} not found.", ct, "delete from");
        if (statusError is not null) return statusError;

        // DB cascade (AipProgram -> AipProject -> AipActivity) removes the whole subtree.
        await _programRepo.DeleteAsync(program, ct);
        await _programRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_programs", program.Id, AuditAction.Delete,
            new { program.OfficeId, program.RefCode, program.Name }, null, ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> DeleteProjectAsync(int projectId, User caller, CancellationToken ct = default)
    {
        AipProject? project = await _aipRepo.GetProjectByIdAsync(projectId, ct);
        if (project is null)
            return ServiceResult<bool>.NotFound($"AIP project {projectId} not found.");

        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null)
            return ServiceResult<bool>.NotFound($"AIP program {project.ProgramId} not found.");
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<bool>.NotFound($"AIP office {program.OfficeId} not found.");

        ServiceResult<bool>? statusError = await CheckWritableAsync<bool>(office, caller, $"AIP project {projectId} not found.", ct, "delete from");
        if (statusError is not null) return statusError;

        // DB cascade (AipProject -> AipActivity) removes the activities under it.
        await _projectRepo.DeleteAsync(project, ct);
        await _projectRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_projects", project.Id, AuditAction.Delete,
            new { project.ProgramId, project.RefCode, project.Name }, null, ct);

        return ServiceResult<bool>.Ok(true);
    }

    public async Task<ServiceResult<bool>> DeleteActivityAsync(int activityId, User caller, CancellationToken ct = default)
    {
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(activityId, ct);
        if (activity is null)
            return ServiceResult<bool>.NotFound($"AIP activity {activityId} not found.");

        AipProject? project = await _aipRepo.GetProjectByIdAsync(activity.ProjectId, ct);
        if (project is null)
            return ServiceResult<bool>.NotFound($"AIP project {activity.ProjectId} not found.");
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null)
            return ServiceResult<bool>.NotFound($"AIP program {project.ProgramId} not found.");
        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (office is null)
            return ServiceResult<bool>.NotFound($"AIP office {program.OfficeId} not found.");

        ServiceResult<bool>? statusError = await CheckWritableAsync<bool>(office, caller, $"AIP activity {activityId} not found.", ct, "delete from");
        if (statusError is not null) return statusError;

        await _activityRepo.DeleteAsync(activity, ct);
        await _activityRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_activities", activity.Id, AuditAction.Delete,
            new { activity.ProjectId, activity.RefCode, activity.Name }, null, ct);

        return ServiceResult<bool>.Ok(true);
    }

    /// <summary>Shared Draft-status guard for the manual-entry Add*/Update*/Delete* methods,
    /// keyed off the AipRecord reached by walking up from whichever node the caller is touching.</summary>
    /// <summary>
    /// The single gate every AIP write passes through: <b>may this caller write this node, and is
    /// its record still editable?</b> Returns null when the write may proceed.
    ///
    /// <para>
    /// ⚠️ <b>The ownership check comes first, and it answers NotFound rather than Forbidden.</b>
    /// <paramref name="notFoundMessage"/> is the caller's own "no such node" message, so a node the
    /// caller may not touch and a node that does not exist are byte-for-byte indistinguishable. A
    /// 403 would confirm that the node exists and belongs to another office — exactly the existence
    /// check the read paths clamp to avoid (V18-39). Clamping, the read side's answer, is not
    /// available here: a write names one node, and redirecting it to a different one would silently
    /// write to the wrong row, which is worse than any refusal.
    /// </para>
    ///
    /// <para>
    /// The cost is a puzzling error for a PPDO admin who mistypes an id. That is a support question;
    /// the alternative is a disclosure.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<T>?> CheckWritableAsync<T>(
        AipOffice office, User caller, string notFoundMessage,
        CancellationToken ct, string action = "add to")
    {
        if (!OfficeScope.Resolve(caller).Permits(office.OfficeId))
            return ServiceResult<T>.NotFound(notFoundMessage);

        AipRecord? rec = await _aipRepo.GetByIntIdAsync(office.AipRecordId, ct);
        if (rec is null)
            return ServiceResult<T>.NotFound($"AIP record {office.AipRecordId} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<T>.BadRequest(
                $"Cannot {action} a '{rec.Status}' record. Unlock it back to Draft first.");
        return null;
    }

    /// <summary>Next zero-padded 3-digit segment appended to <paramref name="parentRefCode"/>,
    /// one past the highest existing sibling suffix (e.g. "...-001-001-002-001" then "...-002").</summary>
    private static string NextRefCode(string parentRefCode, IEnumerable<string> siblingRefCodes)
    {
        int next = siblingRefCodes
            .Select(rc => rc.Split('-')[^1])
            .Select(s => int.TryParse(s, out int n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;
        return $"{parentRefCode}-{next:D3}";
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<ServiceResult<AipRecordDto>> FinalizeAsync(
        int id, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<AipRecordDto>.NotFound($"AIP record {id} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<AipRecordDto>.BadRequest($"Cannot finalize a record with status '{rec.Status}'.");

        rec.Status = PlanningStatus.Final;
        await _aipRepo.UpdateAsync(rec, ct);
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_records", rec.Id, AuditAction.Update,
            new { Status = PlanningStatus.Draft }, new { Status = PlanningStatus.Final }, ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(rec));
    }

    public async Task<ServiceResult<AipRecordDto>> UnlockAsync(
        int id, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<AipRecordDto>.NotFound($"AIP record {id} not found.");
        if (rec.Status != PlanningStatus.Final)
            return ServiceResult<AipRecordDto>.BadRequest($"Cannot unlock a record with status '{rec.Status}'.");

        rec.Status = PlanningStatus.Draft;
        await _aipRepo.UpdateAsync(rec, ct);
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_records", rec.Id, AuditAction.Update,
            new { Status = PlanningStatus.Final }, new { Status = PlanningStatus.Draft }, ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(rec));
    }

    public async Task<ServiceResult<AipRecordDto>> ArchiveAsync(
        int id, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<AipRecordDto>.NotFound($"AIP record {id} not found.");
        if (rec.Status == PlanningStatus.Archived)
            return ServiceResult<AipRecordDto>.BadRequest("Record is already archived.");

        string oldStatus = rec.Status;
        rec.Status = PlanningStatus.Archived;
        await _aipRepo.UpdateAsync(rec, ct);
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_records", rec.Id, AuditAction.Update,
            new { Status = oldStatus }, new { Status = PlanningStatus.Archived }, ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(rec));
    }

    // ── Field updates (v1.4 Q1/Q2 — captured during WFP data entry) ────────────

    public async Task<ServiceResult<AipProgramDto>> UpdateProgramFunctionBandAsync(
        int programId, string? functionBand, User caller, CancellationToken ct = default)
    {
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(programId, ct);
        if (program is null)
            return ServiceResult<AipProgramDto>.NotFound($"AIP program {programId} not found.");

        AipOffice? bandOffice = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (bandOffice is null || !OfficeScope.Resolve(caller).Permits(bandOffice.OfficeId))
            return ServiceResult<AipProgramDto>.NotFound($"AIP program {programId} not found.");

        if (!TryCanonicalizeFunctionBand(functionBand, out string? canonical, out string? error))
            return ServiceResult<AipProgramDto>.BadRequest(error!);

        string? oldValue = program.FunctionBand;
        program.FunctionBand = canonical;
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_programs", program.Id, AuditAction.Update,
            new { FunctionBand = oldValue }, new { FunctionBand = canonical }, ct);

        // Field-update response — Projects intentionally omitted (not re-fetched here); callers
        // must patch their own local state by field, not by replacing the whole program node.
        return ServiceResult<AipProgramDto>.Ok(new AipProgramDto(
            program.Id, program.OfficeId, program.RefCode, program.Name,
            Array.Empty<AipProjectDto>(), program.FunctionBand));
    }

    public async Task<ServiceResult<AipActivityDto>> UpdateActivityIsCreationAsync(
        int activityId, bool isCreation, User caller, CancellationToken ct = default)
    {
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(activityId, ct);
        if (activity is null)
            return ServiceResult<AipActivityDto>.NotFound($"AIP activity {activityId} not found.");

        // The full walk up to the owning office — activity → project → program → AipOffice.
        AipProject? creationProject = await _aipRepo.GetProjectByIdAsync(activity.ProjectId, ct);
        AipProgram? creationProgram = creationProject is null
            ? null : await _aipRepo.GetProgramByIdAsync(creationProject.ProgramId, ct);
        AipOffice?  creationOffice  = creationProgram is null
            ? null : await _aipRepo.GetOfficeByIdAsync(creationProgram.OfficeId, ct);
        if (creationOffice is null || !OfficeScope.Resolve(caller).Permits(creationOffice.OfficeId))
            return ServiceResult<AipActivityDto>.NotFound($"AIP activity {activityId} not found.");

        bool oldValue = activity.IsCreation;
        activity.IsCreation = isCreation;
        await _aipRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("aip_activities", activity.Id, AuditAction.Update,
            new { IsCreation = oldValue }, new { IsCreation = isCreation }, ct);

        return ServiceResult<AipActivityDto>.Ok(MapActivityToDto(activity));
    }

    // ── Purge (dev/test only) ─────────────────────────────────────────────────

    public async Task<int> PurgeAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<AipRecord> all = await _aipRepo.GetAllAsync(ct);
        foreach (AipRecord rec in all)
            await _aipRepo.DeleteAsync(rec, ct);
        if (all.Count > 0)
            await _aipRepo.SaveChangesAsync(ct);
        return all.Count;
    }

    // ── Confirm-import entity builders (RAL-108) ─────────────────────────────
    //
    // A program/project row that also carries its own amounts (e.g. a program with no child
    // project that still records a budget) is materialized here as a synthetic project and/or
    // activity — IsSynthetic = true — rather than as new columns on AipProgram/AipProject.
    // Financial data must always live on an AipActivity so it reaches WFP, reports, and the
    // external AIP API the same way every other activity does.

    private static List<AipProgram> BuildPrograms(
        List<ParsedAipProgramDto> progDtos, Dictionary<string, FundingSource> fsDict) =>
        progDtos.Select(progDto =>
        {
            List<AipProject> projects = BuildProjects(progDto.Projects, fsDict);
            if (progDto.LineItem is not null)
            {
                projects.Add(new AipProject
                {
                    RefCode     = progDto.RefCode,
                    Name        = progDto.Name,
                    IsSynthetic = true,
                    Activities  = [BuildActivity(progDto.LineItem, fsDict, isSynthetic: true)],
                });
            }
            return new AipProgram
            {
                RefCode      = progDto.RefCode,
                Name         = progDto.Name,
                // Function band is required going forward (UpdateProgramFunctionBandAsync
                // rejects null/empty) — default new imports to Core rather than leaving them
                // unset; whoever enters the WFP can change it via the entry wizard.
                FunctionBand = AipFunctionBand.Core,
                Projects     = projects,
            };
        }).ToList();

    private static List<AipProject> BuildProjects(
        List<ParsedAipProjectDto> projDtos, Dictionary<string, FundingSource> fsDict) =>
        projDtos.Select(projDto =>
        {
            List<AipActivity> activities = projDto.Activities
                .Select(actDto => BuildActivity(actDto, fsDict, isSynthetic: false)).ToList();
            if (projDto.LineItem is not null)
                activities.Add(BuildActivity(projDto.LineItem, fsDict, isSynthetic: true));

            return new AipProject { RefCode = projDto.RefCode, Name = projDto.Name, Activities = activities };
        }).ToList();

    private static AipActivity BuildActivity(
        ParsedAipActivityDto actDto, Dictionary<string, FundingSource> fsDict, bool isSynthetic)
    {
        fsDict.TryGetValue(actDto.FundingSourceRaw ?? string.Empty, out FundingSource? fs);
        return new AipActivity
        {
            RefCode               = actDto.RefCode,
            Name                  = actDto.Name,
            EsreCode              = actDto.EsreCode,
            ImplementingOffice    = actDto.ImplementingOffice,
            StartDate             = actDto.StartDate,
            EndDate               = actDto.EndDate,
            ExpectedOutputs       = actDto.ExpectedOutputs,
            FundingSourceId       = fs?.Id,
            FundingSourceSnapshot = fs?.Code ?? actDto.FundingSourceRaw,
            Ps                    = actDto.Ps,
            Mooe                  = actDto.Mooe,
            Co                    = actDto.Co,
            Total                 = actDto.Total,
            CcAdaptation          = actDto.CcAdaptation,
            CcMitigation          = actDto.CcMitigation,
            CcTypologyCode        = actDto.CcTypologyCode,
            IsSynthetic           = isSynthetic,
        };
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static AipRecordDto MapToDto(AipRecord r) => new(
        r.Id, r.FiscalYear, r.OfficeId, r.EntrySource, r.OriginalFilename,
        r.UploadedById, r.UploadedAt, r.Status, r.LdipId, r.SourceId,
        OfficeCount: 0, UploadedByName: null);

    private static AipRecordDto MapToListDto(
        AipRecord r,
        Dictionary<int, int> officeCounts,
        IReadOnlyDictionary<Guid, string> userNames) => new(
        r.Id, r.FiscalYear, r.OfficeId, r.EntrySource, r.OriginalFilename,
        r.UploadedById, r.UploadedAt, r.Status, r.LdipId, r.SourceId,
        OfficeCount: officeCounts.GetValueOrDefault(r.Id, 0),
        UploadedByName: userNames.GetValueOrDefault(r.UploadedById));

    private static AipActivityDto MapActivityToDto(AipActivity a) => new(
        a.Id, a.ProjectId, a.RefCode, a.Name, a.EsreCode, a.ImplementingOffice,
        a.StartDate, a.EndDate, a.ExpectedOutputs, a.FundingSourceId, a.FundingSourceSnapshot,
        a.Ps, a.Mooe, a.Co, a.Total, a.CcAdaptation, a.CcMitigation, a.CcTypologyCode,
        a.IsCreation, a.IsSynthetic);

    /// <summary>The only 3 values <c>function_band</c> may hold (case-insensitive on input, canonicalized on save).</summary>
    private static readonly string[] AllowedFunctionBands =
        { AipFunctionBand.Core, AipFunctionBand.Strategic, AipFunctionBand.Support };

    private static bool TryCanonicalizeFunctionBand(string? input, out string? canonical, out string? error)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            canonical = null;
            error = $"function_band is required and must be one of: {string.Join(", ", AllowedFunctionBands)}.";
            return false;
        }

        string trimmed = input.Trim();
        string? match = AllowedFunctionBands.FirstOrDefault(
            b => b.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            canonical = null;
            error = $"function_band must be one of: {string.Join(", ", AllowedFunctionBands)}.";
            return false;
        }

        canonical = match;
        error = null;
        return true;
    }
}
