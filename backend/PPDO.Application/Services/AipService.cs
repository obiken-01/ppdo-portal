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

    public AipService(
        IAipRepository             aipRepo,
        IRepository<FundingSource>  fsRepo,
        IUserRepository            userRepo,
        IAipXlsmParser parser,
        IAuditService  audit,
        CallerContext  caller,
        IRepository<AipOffice> officeRepo,
        IWfpRepository wfpRepo)
    {
        _aipRepo    = aipRepo;
        _fsRepo     = fsRepo;
        _userRepo   = userRepo;
        _parser     = parser;
        _audit      = audit;
        _caller     = caller;
        _officeRepo = officeRepo;
        _wfpRepo    = wfpRepo;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<AipRecordDto>> GetAllAsync(
        int? fiscalYear, string? status, CancellationToken ct = default)
    {
        IEnumerable<AipRecord> q = await _aipRepo.GetAllAsync(ct);
        if (fiscalYear.HasValue) q = q.Where(r => r.FiscalYear == fiscalYear.Value);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));

        List<AipRecord> records = q.OrderByDescending(r => r.UploadedAt).ToList();

        // Scope office count to only the AIP ids being returned (not the whole table).
        List<int> aipIds = records.Select(r => r.Id).ToList();
        IReadOnlyList<AipOffice> offices = await _aipRepo.GetOfficesByAipIdsAsync(aipIds, ct);
        Dictionary<int, int> officeCounts = offices
            .GroupBy(o => o.AipRecordId)
            .ToDictionary(g => g.Key, g => g.Count());

        // Build user name lookup scoped to only the uploader ids in this result set.
        List<Guid> uploaderIds = records.Select(r => r.UploadedById).Distinct().ToList();
        IReadOnlyDictionary<Guid, string> userNames = await _userRepo.GetNamesByIdsAsync(uploaderIds, ct);

        return records.Select(r => MapToListDto(r, officeCounts, userNames)).ToList();
    }

    public async Task<ServiceResult<AipRecordDetailDto>> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<AipRecordDetailDto>.NotFound($"AIP record {id} not found.");

        // Load each hierarchy level scoped to the ids from the level above.
        IReadOnlyList<AipOffice>   offices  = await _aipRepo.GetOfficesByAipIdAsync(id, ct);
        List<int> officeIds  = offices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram>  programs = await _aipRepo.GetProgramsByOfficeIdsAsync(officeIds, ct);
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
        int id, CancellationToken ct = default)
    {
        AipRecord? rec = await _aipRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<AipRecordSummaryDto>.NotFound($"AIP record {id} not found.");

        IReadOnlyList<AipOffice>   offices  = await _aipRepo.GetOfficesByAipIdAsync(id, ct);
        List<int> officeIds  = offices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram>  programs = await _aipRepo.GetProgramsByOfficeIdsAsync(officeIds, ct);
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
        // Load funding source lookup for snapshot population — needed by both paths below.
        IReadOnlyList<FundingSource> fsList = await _fsRepo.GetAllAsync(ct);
        Dictionary<string, FundingSource> fsDict =
            fsList.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);

        // Re-upload path (RAL-178) — replace an existing record's hierarchy in place.
        // Bypasses the one-active-AIP-per-fiscal-year guard below entirely: that guard exists
        // to stop a SECOND competing record for the year, not the record being replaced (which
        // GetLatestByFiscalYearAsync would otherwise find as a false "conflict" — itself).
        if (dto.TargetRecordId is int targetId)
            return await ReplaceImportAsync(targetId, dto, fsDict, ct);

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
            Offices          = BuildOffices(dto.SectorOffices, fsDict),
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
        int targetId, AipImportConfirmDto dto, Dictionary<string, FundingSource> fsDict, CancellationToken ct)
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
        rec.Offices          = BuildOffices(dto.SectorOffices, fsDict);

        await _aipRepo.UpdateAsync(rec, ct);
        await _aipRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("aip_records", rec.Id, AuditAction.Update,
            old,
            new { rec.FiscalYear, rec.OriginalFilename, OfficeCount = rec.Offices.Count },
            ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(rec));
    }

    private static List<AipOffice> BuildOffices(
        Dictionary<string, List<ParsedAipOfficeDto>> sectorOffices, Dictionary<string, FundingSource> fsDict) =>
        sectorOffices
            .SelectMany(kvp => kvp.Value)
            .Select(officeDto => new AipOffice
            {
                RefCode  = officeDto.RefCode,
                Name     = officeDto.Name,
                Sector   = officeDto.Sector,
                Programs = BuildPrograms(officeDto.Programs, fsDict),
            }).ToList();

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
        int programId, string? functionBand, CancellationToken ct = default)
    {
        AipProgram? program = await _aipRepo.GetProgramByIdAsync(programId, ct);
        if (program is null)
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
        int activityId, bool isCreation, CancellationToken ct = default)
    {
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(activityId, ct);
        if (activity is null)
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
        r.Id, r.FiscalYear, r.EntrySource, r.OriginalFilename,
        r.UploadedById, r.UploadedAt, r.Status, r.LdipId, r.SourceId,
        OfficeCount: 0, UploadedByName: null);

    private static AipRecordDto MapToListDto(
        AipRecord r,
        Dictionary<int, int> officeCounts,
        IReadOnlyDictionary<Guid, string> userNames) => new(
        r.Id, r.FiscalYear, r.EntrySource, r.OriginalFilename,
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
