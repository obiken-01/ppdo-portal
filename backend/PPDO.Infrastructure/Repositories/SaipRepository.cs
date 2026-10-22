using Microsoft.EntityFrameworkCore;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISaipRepository"/> (v1.10.0 — PPDO-246).
///
/// <para>
/// ⚠️ <b>The only <c>IgnoreQueryFilters()</c> on <c>aip_offices</c> in the codebase</b>, and each one
/// is narrowed to a single SAIP's rows in the same expression. Widening one of these, or adding
/// another elsewhere, is how a report would start showing unapproved figures.
/// </para>
/// </summary>
public sealed class SaipRepository : Repository<AipSaip>, ISaipRepository
{
    public SaipRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<AipSaip?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<AipSaip>().FirstOrDefaultAsync(s => s.Id == id, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipSaip>> GetForOfficeAsync(
        int aipRecordId, int officeId, CancellationToken ct = default)
        => await _context.Set<AipSaip>()
            .AsNoTracking()
            .Where(s => s.AipRecordId == aipRecordId && s.OfficeId == officeId)
            .OrderByDescending(s => s.Number)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<AipSaip?> GetOpenAsync(int aipRecordId, int officeId, CancellationToken ct = default)
        => await _context.Set<AipSaip>()
            .Where(s => s.AipRecordId == aipRecordId && s.OfficeId == officeId
                     && s.Status != SaipStatus.Approved && s.Status != SaipStatus.Cancelled)
            .OrderByDescending(s => s.Number)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<int> GetMaxNumberAsync(int aipRecordId, int officeId, CancellationToken ct = default)
        => await _context.Set<AipSaip>()
            .Where(s => s.AipRecordId == aipRecordId && s.OfficeId == officeId)
            .MaxAsync(s => (int?)s.Number, ct) ?? 0;

    /// <inheritdoc />
    public async Task AddDraftCopyAsync(
        IReadOnlyList<AipOffice> groups, IReadOnlyList<AipExpenditure> expenditures, CancellationToken ct = default)
    {
        // AddRange walks the navigation collections, so programs → projects → activities go in with
        // their groups. Expenditure lines are not reachable from an activity (no collection on that
        // side), so they are added themselves; each one's Activity reference resolves its FK on save.
        await _context.Set<AipOffice>().AddRangeAsync(groups, ct);
        await _context.Set<AipExpenditure>().AddRangeAsync(expenditures, ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AipOffice>> GetDraftOfficesAsync(int saipId, CancellationToken ct = default)
        => await _context.Set<AipOffice>()
            .IgnoreQueryFilters()           // ⚠️ SAIP path only — narrowed to this SAIP below.
            .AsNoTracking()
            .Where(o => o.SaipId == saipId)
            .OrderBy(o => o.RefCode).ThenBy(o => o.Id)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<int> DeleteDraftCopyAsync(int saipId, CancellationToken ct = default)
        // ⚠️ IgnoreQueryFilters is required here, not incidental: under the filter this WHERE would
        // read "saip_id IS NULL AND saip_id = @id" and delete nothing, leaving the copy behind
        // (SaipCopySqlServerTests.Cancel_RemovesTheWholeCopyAndNothingElse is red without it).
        // The children go by the database's ON DELETE CASCADE from aip_offices down to items.
        => await _context.Set<AipOffice>()
            .IgnoreQueryFilters()
            .Where(o => o.SaipId == saipId)
            .ExecuteDeleteAsync(ct);
}
