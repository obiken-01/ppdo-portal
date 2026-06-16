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
/// </summary>
public sealed class AipService : IAipService
{
    private readonly IRepository<AipRecord>    _aipRepo;
    private readonly IRepository<AipOffice>    _officeRepo;
    private readonly IRepository<AipProgram>   _programRepo;
    private readonly IRepository<AipProject>   _projectRepo;
    private readonly IRepository<AipActivity>  _actRepo;
    private readonly IRepository<FundingSource> _fsRepo;
    private readonly IRepository<User>         _userRepo;
    private readonly IAipXlsmParser _parser;
    private readonly IAuditService  _audit;
    private readonly CallerContext  _caller;

    public AipService(
        IRepository<AipRecord>     aipRepo,
        IRepository<AipOffice>     officeRepo,
        IRepository<AipProgram>    programRepo,
        IRepository<AipProject>    projectRepo,
        IRepository<AipActivity>   actRepo,
        IRepository<FundingSource>  fsRepo,
        IRepository<User>          userRepo,
        IAipXlsmParser parser,
        IAuditService  audit,
        CallerContext  caller)
    {
        _aipRepo     = aipRepo;
        _officeRepo  = officeRepo;
        _programRepo = programRepo;
        _projectRepo = projectRepo;
        _actRepo     = actRepo;
        _fsRepo      = fsRepo;
        _userRepo    = userRepo;
        _parser      = parser;
        _audit       = audit;
        _caller      = caller;
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

        // Build office-count lookup: aip_record_id → count of aip_offices rows.
        IReadOnlyList<AipOffice> allOffices = await _officeRepo.GetAllAsync(ct);
        Dictionary<int, int> officeCounts = allOffices
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
        AipRecord? rec = (await _aipRepo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
        if (rec is null)
            return ServiceResult<AipRecordDetailDto>.NotFound($"AIP record {id} not found.");

        // Load hierarchy levels separately (no deep Include chains).
        IReadOnlyList<AipOffice>   offices  = (await _officeRepo.GetAllAsync(ct)).Where(o => o.AipRecordId == id).ToList();
        List<int> officeIds = offices.Select(o => o.Id).ToList();
        IReadOnlyList<AipProgram>  programs = (await _programRepo.GetAllAsync(ct)).Where(p => officeIds.Contains(p.OfficeId)).ToList();
        List<int> programIds = programs.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject>  projects = (await _projectRepo.GetAllAsync(ct)).Where(j => programIds.Contains(j.ProgramId)).ToList();
        List<int> projectIds = projects.Select(j => j.Id).ToList();
        IReadOnlyList<AipActivity> acts     = (await _actRepo.GetAllAsync(ct)).Where(a => projectIds.Contains(a.ProjectId)).ToList();

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
        // Load funding source lookup for snapshot population.
        IReadOnlyList<FundingSource> fsList = await _fsRepo.GetAllAsync(ct);
        Dictionary<string, FundingSource> fsDict =
            fsList.ToDictionary(f => f.Code, StringComparer.OrdinalIgnoreCase);

        DateTime now = DateTime.UtcNow;
        AipRecord aipRecord = new()
        {
            FiscalYear       = dto.FiscalYear,
            EntrySource      = "Upload",
            OriginalFilename = dto.OriginalFilename,
            UploadedById     = uploadedById,
            UploadedAt       = now,
            Status           = PlanningStatus.Draft,
            LdipId           = dto.LdipId,
        };

        await _aipRepo.AddAsync(aipRecord, ct);
        await _aipRepo.SaveChangesAsync(ct); // generate AipRecord.Id

        // Walk and persist hierarchy.
        foreach ((_, List<ParsedAipOfficeDto> offices) in dto.SectorOffices)
        {
            foreach (ParsedAipOfficeDto officeDto in offices)
            {
                AipOffice office = new()
                {
                    AipRecordId = aipRecord.Id,
                    RefCode     = officeDto.RefCode,
                    Name        = officeDto.Name,
                    Sector      = officeDto.Sector,
                };
                await _officeRepo.AddAsync(office, ct);
                await _officeRepo.SaveChangesAsync(ct);

                foreach (ParsedAipProgramDto progDto in officeDto.Programs)
                {
                    AipProgram program = new()
                    {
                        OfficeId = office.Id,
                        RefCode  = progDto.RefCode,
                        Name     = progDto.Name,
                    };
                    await _programRepo.AddAsync(program, ct);
                    await _programRepo.SaveChangesAsync(ct);

                    foreach (ParsedAipProjectDto projDto in progDto.Projects)
                    {
                        AipProject project = new()
                        {
                            ProgramId = program.Id,
                            RefCode   = projDto.RefCode,
                            Name      = projDto.Name,
                        };
                        await _projectRepo.AddAsync(project, ct);
                        await _projectRepo.SaveChangesAsync(ct);

                        foreach (ParsedAipActivityDto actDto in projDto.Activities)
                        {
                            fsDict.TryGetValue(actDto.FundingSourceRaw ?? string.Empty,
                                out FundingSource? fs);

                            AipActivity activity = new()
                            {
                                ProjectId              = project.Id,
                                RefCode                = actDto.RefCode,
                                Name                   = actDto.Name,
                                EsreCode               = actDto.EsreCode,
                                ImplementingOffice     = actDto.ImplementingOffice,
                                StartDate              = actDto.StartDate,
                                EndDate                = actDto.EndDate,
                                ExpectedOutputs        = actDto.ExpectedOutputs,
                                FundingSourceId        = fs?.Id,
                                FundingSourceSnapshot  = fs?.Code ?? actDto.FundingSourceRaw,
                                Ps                     = actDto.Ps,
                                Mooe                   = actDto.Mooe,
                                Co                     = actDto.Co,
                                Total                  = actDto.Total,
                                CcAdaptation           = actDto.CcAdaptation,
                                CcMitigation           = actDto.CcMitigation,
                                CcTypologyCode         = actDto.CcTypologyCode,
                            };
                            await _actRepo.AddAsync(activity, ct);
                        }
                    }
                }
            }
        }
        await _actRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("aip_records", aipRecord.Id, AuditAction.Create,
            null, new { aipRecord.FiscalYear, aipRecord.EntrySource, aipRecord.Status }, ct);

        return ServiceResult<AipRecordDto>.Ok(MapToDto(aipRecord));
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<ServiceResult<AipRecordDto>> FinalizeAsync(
        int id, CancellationToken ct = default)
    {
        AipRecord? rec = (await _aipRepo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
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
        AipRecord? rec = (await _aipRepo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
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
        AipRecord? rec = (await _aipRepo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
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

    // Used by status-transition methods where office count / uploader name are not needed.
    private static AipRecordDto MapToDto(AipRecord r) => new(
        r.Id, r.FiscalYear, r.EntrySource, r.OriginalFilename,
        r.UploadedById, r.UploadedAt, r.Status, r.LdipId, r.SourceId,
        OfficeCount: 0, UploadedByName: null);

    // Used by GetAllAsync — populates office count and uploader display name.
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
