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
    private readonly IRepository<User>         _userRepo;
    private readonly IAipXlsmParser _parser;
    private readonly IAuditService  _audit;
    private readonly CallerContext  _caller;

    public AipService(
        IAipRepository             aipRepo,
        IRepository<FundingSource>  fsRepo,
        IRepository<User>          userRepo,
        IAipXlsmParser parser,
        IAuditService  audit,
        CallerContext  caller)
    {
        _aipRepo  = aipRepo;
        _fsRepo   = fsRepo;
        _userRepo = userRepo;
        _parser   = parser;
        _audit    = audit;
        _caller   = caller;
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

        // Build user name lookup: user_id → full name.
        IReadOnlyList<User> allUsers = await _userRepo.GetAllAsync(ct);
        Dictionary<Guid, string> userNames = allUsers.ToDictionary(u => u.Id, u => u.FullName);

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
                            acts.Where(a => a.ProjectId == j.Id).Select(MapActivityToDto).ToList()))
                        .ToList();
                    return new AipProgramDto(p.Id, p.OfficeId, p.RefCode, p.Name, projDtos);
                })
                .ToList();
            return new AipOfficeDto(o.Id, o.AipRecordId, o.RefCode, o.Name, o.Sector, progDtos);
        }).ToList();

        AipRecordDetailDto detail = new(
            rec.Id, rec.FiscalYear, rec.EntrySource, rec.OriginalFilename,
            rec.UploadedById, rec.UploadedAt, rec.Status, rec.LdipId, rec.SourceId, officeDtos);

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
                                    a.FundingSourceId, a.FundingSourceSnapshot))
                                .ToList()))
                        .ToList();
                    return new AipProgramSummaryDto(p.Id, p.RefCode, p.Name, projDtos);
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
                        List<ParsedAipActivityDto> actDtos = [];
                        foreach (ParsedAipActivity act in proj.Activities)
                        {
                            activityCount++;
                            if (!string.IsNullOrWhiteSpace(act.FundingSourceRaw) &&
                                !fsDict.ContainsKey(act.FundingSourceRaw))
                            {
                                warnings.Add(
                                    $"Activity {act.RefCode}: unmatched funding source '{act.FundingSourceRaw}'.");
                            }
                            actDtos.Add(new ParsedAipActivityDto(
                                act.RefCode, act.Name, act.EsreCode, act.ImplementingOffice,
                                act.StartDate, act.EndDate, act.ExpectedOutputs, act.FundingSourceRaw,
                                act.Ps, act.Mooe, act.Co, act.Total,
                                act.CcAdaptation, act.CcMitigation, act.CcTypologyCode));
                        }
                        projDtos.Add(new ParsedAipProjectDto(proj.RefCode, proj.Name, actDtos));
                    }
                    progDtos.Add(new ParsedAipProgramDto(prog.RefCode, prog.Name, projDtos));
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
        // Guard: only one active (Draft or Final) AIP per fiscal year.
        IReadOnlyList<AipRecord> all = await _aipRepo.GetAllAsync(ct);
        AipRecord? conflict = all.FirstOrDefault(
            r => r.FiscalYear == dto.FiscalYear && r.Status != PlanningStatus.Archived);
        if (conflict is not null)
        {
            string hint = conflict.Status == PlanningStatus.Draft
                ? "Archive the existing record first before uploading a new one."
                : "The existing record must be unlocked by an admin before a new upload is allowed.";
            return ServiceResult<AipRecordDto>.BadRequest(
                $"An AIP for FY {dto.FiscalYear} already exists with status '{conflict.Status}'. {hint}");
        }

        // Load funding source lookup for snapshot population.
        IReadOnlyList<FundingSource> fsList = await _fsRepo.GetAllAsync(ct);
        Dictionary<string, FundingSource> fsDict =
            fsList.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);

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
            Offices          = dto.SectorOffices
                .SelectMany(kvp => kvp.Value)
                .Select(officeDto => new AipOffice
                {
                    RefCode  = officeDto.RefCode,
                    Name     = officeDto.Name,
                    Sector   = officeDto.Sector,
                    Programs = officeDto.Programs.Select(progDto => new AipProgram
                    {
                        RefCode  = progDto.RefCode,
                        Name     = progDto.Name,
                        Projects = progDto.Projects.Select(projDto => new AipProject
                        {
                            RefCode    = projDto.RefCode,
                            Name       = projDto.Name,
                            Activities = projDto.Activities.Select(actDto =>
                            {
                                fsDict.TryGetValue(actDto.FundingSourceRaw ?? string.Empty,
                                    out FundingSource? fs);
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
                                };
                            }).ToList(),
                        }).ToList(),
                    }).ToList(),
                }).ToList(),
        };

        await _aipRepo.AddAsync(aipRecord, ct);
        await _aipRepo.SaveChangesAsync(ct); // single transaction — all-or-nothing

        await _audit.LogAsync("aip_records", aipRecord.Id, AuditAction.Create,
            null, new { aipRecord.FiscalYear, aipRecord.EntrySource, aipRecord.Status }, ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(aipRecord));
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

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static AipRecordDto MapToDto(AipRecord r) => new(
        r.Id, r.FiscalYear, r.EntrySource, r.OriginalFilename,
        r.UploadedById, r.UploadedAt, r.Status, r.LdipId, r.SourceId,
        OfficeCount: 0, UploadedByName: null);

    private static AipRecordDto MapToListDto(
        AipRecord r,
        Dictionary<int, int> officeCounts,
        Dictionary<Guid, string> userNames) => new(
        r.Id, r.FiscalYear, r.EntrySource, r.OriginalFilename,
        r.UploadedById, r.UploadedAt, r.Status, r.LdipId, r.SourceId,
        OfficeCount: officeCounts.GetValueOrDefault(r.Id, 0),
        UploadedByName: userNames.GetValueOrDefault(r.UploadedById));

    private static AipActivityDto MapActivityToDto(AipActivity a) => new(
        a.Id, a.ProjectId, a.RefCode, a.Name, a.EsreCode, a.ImplementingOffice,
        a.StartDate, a.EndDate, a.ExpectedOutputs, a.FundingSourceId, a.FundingSourceSnapshot,
        a.Ps, a.Mooe, a.Co, a.Total, a.CcAdaptation, a.CcMitigation, a.CcTypologyCode);
}
