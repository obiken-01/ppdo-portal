using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// LDIP service — CRUD + status lifecycle (RAL-64).
/// Ref-code format: LDIP-{FY_START}-{seq:D3} (sequence per fiscal year, across all statuses).
/// Edit guard: only Draft records may be updated.
/// Status transitions enforced here; admin check for Unlock is done in the Functions layer.
/// </summary>
public sealed class LdipService : ILdipService
{
    private readonly IRepository<LdipRecord> _repo;
    private readonly IAuditService _audit;
    private readonly CallerContext _caller;

    public LdipService(IRepository<LdipRecord> repo, IAuditService audit, CallerContext caller)
    {
        _repo   = repo;
        _audit  = audit;
        _caller = caller;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<LdipRecordDto>> GetAllAsync(
        string? status, CancellationToken ct = default)
    {
        IEnumerable<LdipRecord> q = await _repo.GetAllAsync(ct);
        if (!string.IsNullOrWhiteSpace(status))
            q = q.Where(r => r.Status.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
        return q.OrderByDescending(r => r.CreatedAt).Select(MapToDto).ToList();
    }

    public async Task<ServiceResult<LdipRecordDto>> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = (await _repo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
        return rec is null
            ? ServiceResult<LdipRecordDto>.NotFound($"LDIP record {id} not found.")
            : ServiceResult<LdipRecordDto>.Ok(MapToDto(rec));
    }

    // ── Create ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<LdipRecordDto>> CreateAsync(
        CreateLdipDto dto, Guid createdById, CancellationToken ct = default)
    {
        IReadOnlyList<LdipRecord> all = await _repo.GetAllAsync(ct);
        int seq = all.Count(r => r.FiscalYearStart == dto.FiscalYearStart) + 1;
        string refCode = $"LDIP-{dto.FiscalYearStart}-{seq:D3}";

        DateTime now = DateTime.UtcNow;
        LdipRecord entity = new()
        {
            RefCode        = refCode,
            Title          = dto.Title,
            FiscalYearStart = dto.FiscalYearStart,
            FiscalYearEnd   = dto.FiscalYearEnd,
            EntryMode      = dto.EntryMode,
            Status         = PlanningStatus.Draft,
            CreatedById    = createdById,
            CreatedAt      = now,
            UpdatedAt      = now,
        };

        await _repo.AddAsync(entity, ct);
        await _repo.SaveChangesAsync(ct);

        await _audit.LogAsync("ldip_records", entity.Id, AuditAction.Create,
            null, new { entity.RefCode, entity.Title, entity.Status }, ct);

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(entity));
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<LdipRecordDto>> UpdateAsync(
        int id, UpdateLdipDto dto, CancellationToken ct = default)
    {
        LdipRecord? rec = (await _repo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
        if (rec is null)
            return ServiceResult<LdipRecordDto>.NotFound($"LDIP record {id} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<LdipRecordDto>.BadRequest("Cannot edit a Final or Archived LDIP record.");

        object old = new { rec.Title, rec.FiscalYearStart, rec.FiscalYearEnd, rec.EntryMode };
        rec.Title           = dto.Title;
        rec.FiscalYearStart = dto.FiscalYearStart;
        rec.FiscalYearEnd   = dto.FiscalYearEnd;
        rec.EntryMode       = dto.EntryMode;
        rec.UpdatedAt       = DateTime.UtcNow;

        await _repo.UpdateAsync(rec, ct);
        await _repo.SaveChangesAsync(ct);
        await _audit.LogAsync("ldip_records", rec.Id, AuditAction.Update,
            old, new { rec.Title, rec.FiscalYearStart, rec.FiscalYearEnd, rec.EntryMode }, ct);

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(rec));
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<ServiceResult<LdipRecordDto>> FinalizeAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = (await _repo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
        if (rec is null)
            return ServiceResult<LdipRecordDto>.NotFound($"LDIP record {id} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<LdipRecordDto>.BadRequest($"Cannot finalize a record with status '{rec.Status}'.");

        rec.Status    = PlanningStatus.Final;
        rec.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(rec, ct);
        await _repo.SaveChangesAsync(ct);
        await _audit.LogAsync("ldip_records", rec.Id, AuditAction.Update,
            new { Status = PlanningStatus.Draft }, new { Status = PlanningStatus.Final }, ct);

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(rec));
    }

    public async Task<ServiceResult<LdipRecordDto>> UnlockAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = (await _repo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
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

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(rec));
    }

    public async Task<ServiceResult<LdipRecordDto>> ArchiveAsync(
        int id, CancellationToken ct = default)
    {
        LdipRecord? rec = (await _repo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
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

        return ServiceResult<LdipRecordDto>.Ok(MapToDto(rec));
    }

    // ── Purge (dev/test only) ─────────────────────────────────────────────────

    public async Task<int> PurgeAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<LdipRecord> all = await _repo.GetAllAsync(ct);
        foreach (LdipRecord rec in all)
            await _repo.DeleteAsync(rec, ct);
        if (all.Count > 0)
            await _repo.SaveChangesAsync(ct);
        return all.Count;
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static LdipRecordDto MapToDto(LdipRecord r) => new(
        r.Id, r.RefCode, r.Title, r.FiscalYearStart, r.FiscalYearEnd,
        r.EntryMode, r.Status, r.SourceId, r.CreatedById, r.CreatedAt, r.UpdatedAt);
}
