using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// LDIP service — CRUD + status lifecycle (RAL-64) + office scoping and the
/// sector-grouped program hierarchy (RAL-61).
///
/// Document ref-code format: LDIP-{FY_START}-{seq:D3} (sequence per fiscal year).
///
/// AIP ref codes are SERVER-AUTHORITATIVE — never taken from the client:
///   group   = "{sector prefix}-000-1-{office.OfficeRefCode}"  (e.g. "1000-000-1-01-010")
///   program = "{group ref code}-{NNN}"                         (contiguous, 001-based)
/// Updates full-replace the hierarchy, so removals renumber the remaining
/// programs with no gaps — correct ref codes are a hard project requirement.
///
/// Program budgets are stored in thousands (₱000), like AIP totals.
/// Edit guard: only Draft records may be updated. Finalize additionally validates
/// completeness (office set, year order, ≥1 program) — the WFP finalize pattern.
/// </summary>
public sealed class LdipService : ILdipService
{
    private static readonly Dictionary<string, string> SectorPrefixes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["General"]  = "1000",
            ["Social"]   = "3000",
            ["Economic"] = "8000",
            ["Others"]   = "9000",
        };

    private readonly ILdipRepository _repo;
    private readonly IRepository<Office> _officeRepo;
    private readonly IAuditService _audit;
    private readonly CallerContext _caller;

    public LdipService(
        ILdipRepository repo,
        IRepository<Office> officeRepo,
        IAuditService audit,
        CallerContext caller)
    {
        _repo       = repo;
        _officeRepo = officeRepo;
        _audit      = audit;
        _caller     = caller;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<LdipRecordDto>> GetAllAsync(
        string? status, int? officeId, CancellationToken ct = default)
    {
        IReadOnlyList<LdipRecord> records = await _repo.GetListAsync(officeId, status, ct);

        // Program counts for the list rows — one query per record would be N+1;
        // load each record's groups only when the list is small (it is: one doc
        // per office per planning period). Kept simple and correct.
        List<LdipRecordDto> result = [];
        foreach (LdipRecord rec in records)
        {
            IReadOnlyList<LdipOffice> groups = await _repo.GetOfficeGroupsAsync(rec.Id, ct);
            result.Add(MapToDto(rec, groups.Sum(g => g.Programs.Count)));
        }
        return result;
    }

    public async Task<ServiceResult<LdipRecordDetailDto>> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = await _repo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<LdipRecordDetailDto>.NotFound($"LDIP record {id} not found.");

        IReadOnlyList<LdipOffice> groups = await _repo.GetOfficeGroupsAsync(id, ct);
        return ServiceResult<LdipRecordDetailDto>.Ok(MapToDetail(rec, groups));
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<LdipRecordDetailDto>> CreateAsync(
        CreateLdipDto dto, Guid createdById, CancellationToken ct = default)
    {
        ServiceResult<Office> officeCheck = await ResolveOfficeAsync(dto.OfficeId, ct);
        if (!officeCheck.IsSuccess)
            return ServiceResult<LdipRecordDetailDto>.FromError(officeCheck);
        Office office = officeCheck.Value!;

        IReadOnlyList<SaveLdipGroupDto> groups = dto.Groups ?? [];
        string? groupError = ValidateGroups(groups);
        if (groupError is not null)
            return ServiceResult<LdipRecordDetailDto>.BadRequest(groupError);

        IReadOnlyList<LdipRecord> all = await _repo.GetAllAsync(ct);
        int seq = all.Count(r => r.FiscalYearStart == dto.FiscalYearStart) + 1;
        string refCode = $"LDIP-{dto.FiscalYearStart}-{seq:D3}";

        DateTime now = DateTime.UtcNow;
        LdipRecord entity = new()
        {
            RefCode         = refCode,
            Title           = ResolveTitle(dto.Title, dto.FiscalYearStart, dto.FiscalYearEnd, office),
            FiscalYearStart = dto.FiscalYearStart,
            FiscalYearEnd   = dto.FiscalYearEnd,
            EntryMode       = dto.EntryMode,
            Status          = PlanningStatus.Draft,
            OfficeId        = office.Id,
            CreatedById     = createdById,
            CreatedAt       = now,
            UpdatedAt       = now,
            Offices         = BuildHierarchy(groups, office),
        };

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        await _audit.LogAsync("ldip_records", entity.Id, AuditAction.Create,
            null,
            new { entity.RefCode, entity.Title, entity.Status, entity.OfficeId,
                  Programs = entity.Offices.Sum(o => o.Programs.Count) },
            ct);

        entity.Office = office;
        return ServiceResult<LdipRecordDetailDto>.Ok(MapToDetail(entity, [.. entity.Offices]));
    }

    // ── Update (full-replace of the hierarchy) ────────────────────────────────

    public async Task<ServiceResult<LdipRecordDetailDto>> UpdateAsync(
        int id, UpdateLdipDto dto, CancellationToken ct = default)
    {
        LdipRecord? rec = await _repo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<LdipRecordDetailDto>.NotFound($"LDIP record {id} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<LdipRecordDetailDto>.BadRequest("Cannot edit a Final or Archived LDIP record.");

        ServiceResult<Office> officeCheck = await ResolveOfficeAsync(dto.OfficeId ?? rec.OfficeId, ct);
        if (!officeCheck.IsSuccess)
            return ServiceResult<LdipRecordDetailDto>.FromError(officeCheck);
        Office office = officeCheck.Value!;

        IReadOnlyList<SaveLdipGroupDto> groups = dto.Groups ?? [];
        string? groupError = ValidateGroups(groups);
        if (groupError is not null)
            return ServiceResult<LdipRecordDetailDto>.BadRequest(groupError);

        object old = new { rec.Title, rec.FiscalYearStart, rec.FiscalYearEnd, rec.EntryMode, rec.OfficeId };

        // Delete-then-reinsert in two SaveChanges rounds so the unique
        // (ldip_record_id, ref_code) index never sees old+new rows side by side.
        IReadOnlyList<LdipOffice> existing = await _repo.GetOfficeGroupsAsync(id, ct);
        foreach (LdipOffice group in existing)
            await _repo.DeleteOfficeGroupAsync(group, ct);
        await _repo.SaveChangesAsync(ct);

        rec.Title           = ResolveTitle(dto.Title, dto.FiscalYearStart, dto.FiscalYearEnd, office);
        rec.FiscalYearStart = dto.FiscalYearStart;
        rec.FiscalYearEnd   = dto.FiscalYearEnd;
        rec.EntryMode       = dto.EntryMode;
        rec.OfficeId        = office.Id;
        rec.UpdatedAt       = DateTime.UtcNow;

        List<LdipOffice> rebuilt = BuildHierarchy(groups, office);
        foreach (LdipOffice group in rebuilt)
        {
            group.LdipRecordId = rec.Id;
            await _repo.AddOfficeGroupAsync(group, ct);
        }

        await _repo.UpdateAsync(rec, ct);
        await _repo.SaveChangesAsync(ct);

        await _audit.LogAsync("ldip_records", rec.Id, AuditAction.Update,
            old,
            new { rec.Title, rec.FiscalYearStart, rec.FiscalYearEnd, rec.EntryMode, rec.OfficeId,
                  Programs = rebuilt.Sum(o => o.Programs.Count) },
            ct);

        rec.Office = office;
        return ServiceResult<LdipRecordDetailDto>.Ok(MapToDetail(rec, rebuilt));
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<ServiceResult<LdipRecordDto>> FinalizeAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = await _repo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<LdipRecordDto>.NotFound($"LDIP record {id} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<LdipRecordDto>.BadRequest($"Cannot finalize a record with status '{rec.Status}'.");

        // Completeness checks live on Finalize (the WFP pattern) — drafts save freely.
        if (rec.OfficeId is null)
            return ServiceResult<LdipRecordDto>.BadRequest("Cannot finalize: no office is set on this record.");
        if (rec.FiscalYearStart > rec.FiscalYearEnd)
            return ServiceResult<LdipRecordDto>.BadRequest("Cannot finalize: year start must be on or before year end.");

        IReadOnlyList<LdipOffice> groups = await _repo.GetOfficeGroupsAsync(id, ct);
        if (!groups.Any(g => g.Programs.Count > 0))
            return ServiceResult<LdipRecordDto>.BadRequest("Cannot finalize: add at least one program first.");

        rec.Status    = PlanningStatus.Final;
        rec.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(rec, ct);
        await _repo.SaveChangesAsync(ct);
        await _audit.LogAsync("ldip_records", rec.Id, AuditAction.Update,
            new { Status = PlanningStatus.Draft }, new { Status = PlanningStatus.Final }, ct);

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(rec, groups.Sum(g => g.Programs.Count)));
    }

    public async Task<ServiceResult<LdipRecordDto>> UnlockAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = await _repo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<LdipRecordDto>.NotFound($"LDIP record {id} not found.");
        if (rec.Status != PlanningStatus.Final)
            return ServiceResult<LdipRecordDto>.BadRequest($"Cannot unlock a record with status '{rec.Status}'.");

        rec.Status    = PlanningStatus.Draft;
        rec.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(rec, ct);
        await _repo.SaveChangesAsync(ct);
        await _audit.LogAsync("ldip_records", rec.Id, AuditAction.Update,
            new { Status = PlanningStatus.Final }, new { Status = PlanningStatus.Draft }, ct);

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(rec, 0));
    }

    public async Task<ServiceResult<LdipRecordDto>> ArchiveAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = await _repo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<LdipRecordDto>.NotFound($"LDIP record {id} not found.");
        if (rec.Status == PlanningStatus.Archived)
            return ServiceResult<LdipRecordDto>.BadRequest("Record is already archived.");

        string oldStatus = rec.Status;
        rec.Status    = PlanningStatus.Archived;
        rec.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(rec, ct);
        await _repo.SaveChangesAsync(ct);
        await _audit.LogAsync("ldip_records", rec.Id, AuditAction.Update,
            new { Status = oldStatus }, new { Status = PlanningStatus.Archived }, ct);

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(rec, 0));
    }

    // ── Purge (dev/test only) ─────────────────────────────────────────────────

    public async Task<int> PurgeAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<LdipRecord> all = await _repo.GetAllAsync(ct);
        foreach (LdipRecord rec in all)
            await _repo.DeleteAsync(rec, ct);   // hierarchy cascades at the DB level
        if (all.Count > 0)
            await _repo.SaveChangesAsync(ct);
        return all.Count;
    }

    // ── Validation / ref-code helpers ─────────────────────────────────────────

    private async Task<ServiceResult<Office>> ResolveOfficeAsync(int? officeId, CancellationToken ct)
    {
        if (officeId is null)
            return ServiceResult<Office>.BadRequest("Office is required.");

        Office? office = (await _officeRepo.GetAllAsync(ct)).FirstOrDefault(o => o.Id == officeId);
        if (office is null)
            return ServiceResult<Office>.NotFound($"Office {officeId} not found.");
        if (string.IsNullOrWhiteSpace(office.OfficeRefCode))
            return ServiceResult<Office>.BadRequest(
                $"Office '{office.OfficeCode}' has no AIP ref code configured. Set office_ref_code in Config → Offices first.");
        return ServiceResult<Office>.Ok(office);
    }

    private static string? ValidateGroups(IReadOnlyList<SaveLdipGroupDto> groups)
    {
        // A sector may appear multiple times — one row per SUB-OFFICE group (e.g.
        // PGO - WARDEN / - AKAP-HUB / - HOUSING all under Social), same as real
        // AIP files. Identity within a record is the (sector, name) pair.
        HashSet<string> seenGroups = new(StringComparer.OrdinalIgnoreCase);
        foreach (SaveLdipGroupDto group in groups)
        {
            if (!SectorPrefixes.ContainsKey(group.Sector))
                return $"Unknown sector '{group.Sector}'. Expected General, Social, Economic, or Others.";
            if (string.IsNullOrWhiteSpace(group.Name))
                return $"Office/sub-office name is required for sector '{group.Sector}'.";
            if (!seenGroups.Add($"{Normalize(group.Sector)}|{group.Name.Trim()}"))
                return $"Duplicate group '{group.Name.Trim()}' under sector '{group.Sector}' — merge its programs into one group.";
            if (group.Programs.Any(p => string.IsNullOrWhiteSpace(p.Name)))
                return $"Every program under sector '{group.Sector}' needs a name.";
        }
        return null;
    }

    /// <summary>
    /// Builds the LdipOffice/LdipProgram hierarchy with server-computed ref codes.
    /// Program numbering is continuous PER REF CODE across groups (WARDEN gets -001,
    /// AKAP-HUB continues -002, …) — groups sharing a ref code must not both start
    /// at -001, matching how real AIP files number sub-office programs.
    /// </summary>
    private static List<LdipOffice> BuildHierarchy(IReadOnlyList<SaveLdipGroupDto> groups, Office office)
    {
        List<LdipOffice> result = [];
        Dictionary<string, int> nextSeqByRefCode = [];
        foreach (SaveLdipGroupDto group in groups)
        {
            string groupRef = $"{SectorPrefixes[group.Sector]}-000-1-{office.OfficeRefCode}";
            // Names are normalised to UPPERCASE — matching how office and program
            // rows are written in the source AIP files.
            LdipOffice entity = new()
            {
                RefCode = groupRef,
                Name    = group.Name.Trim().ToUpperInvariant(),
                Sector  = Normalize(group.Sector),
            };
            foreach (SaveLdipProgramDto program in group.Programs)
            {
                int seq = nextSeqByRefCode.GetValueOrDefault(groupRef, 0) + 1;
                nextSeqByRefCode[groupRef] = seq;
                entity.Programs.Add(new LdipProgram
                {
                    RefCode = $"{groupRef}-{seq:D3}",
                    Name    = program.Name.Trim().ToUpperInvariant(),
                    Budget  = program.Budget,
                });
            }
            result.Add(entity);
        }
        return result;
    }

    private static string Normalize(string sector) =>
        SectorPrefixes.Keys.First(k => k.Equals(sector, StringComparison.OrdinalIgnoreCase));

    private static string ResolveTitle(string title, int start, int end, Office office) =>
        string.IsNullOrWhiteSpace(title)
            ? $"LDIP {start}-{end} — {office.OfficeCode}"
            : title.Trim();

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static LdipRecordDto MapToDto(LdipRecord r, int programCount) => new(
        r.Id, r.RefCode, r.Title, r.FiscalYearStart, r.FiscalYearEnd,
        r.EntryMode, r.Status, r.SourceId, r.CreatedById, r.CreatedAt, r.UpdatedAt,
        r.OfficeId, r.Office?.OfficeName, programCount);

    private static LdipRecordDetailDto MapToDetail(LdipRecord r, IReadOnlyList<LdipOffice> groups) => new(
        r.Id, r.RefCode, r.Title, r.FiscalYearStart, r.FiscalYearEnd,
        r.EntryMode, r.Status, r.SourceId, r.CreatedById, r.CreatedAt, r.UpdatedAt,
        r.OfficeId, r.Office?.OfficeName,
        groups.Select(g => new LdipOfficeGroupDto(
            g.Id, g.RefCode, g.Name, g.Sector,
            g.Programs.OrderBy(p => p.RefCode)
                .Select(p => new LdipProgramDto(p.Id, p.RefCode, p.Name, p.Budget))
                .ToList()))
            .ToList());
}
