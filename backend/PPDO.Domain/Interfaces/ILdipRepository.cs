using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped LDIP reads (RAL-61).
/// Query methods apply WHERE filters in SQL — the table is never fully
/// materialised just to find one record or one office's records.
/// </summary>
public interface ILdipRepository : IRepository<LdipRecord>
{
    /// <summary>
    /// Returns the LDIP record whose integer PK equals <paramref name="id"/> (with the
    /// Office navigation loaded), or null. Needed because the base
    /// <see cref="IRepository{T}.GetByIdAsync"/> uses a Guid key.
    /// </summary>
    Task<LdipRecord?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// LDIP records filtered in SQL by optional office and status, Office navigation
    /// loaded, newest first.
    /// </summary>
    Task<IReadOnlyList<LdipRecord>> GetListAsync(
        int? officeId, string? status, CancellationToken ct = default);

    /// <summary>Sector groups (with their Programs) for one LDIP record, in ref-code order.</summary>
    Task<IReadOnlyList<LdipOffice>> GetOfficeGroupsAsync(
        int ldipRecordId, CancellationToken ct = default);

    /// <summary>Marks a sector group for deletion (programs cascade at the DB level).</summary>
    Task DeleteOfficeGroupAsync(LdipOffice group, CancellationToken ct = default);

    /// <summary>Adds a new sector group (with its Programs attached).</summary>
    Task AddOfficeGroupAsync(LdipOffice group, CancellationToken ct = default);

    /// <summary>
    /// Count of LDIP records whose FiscalYearStart equals <paramref name="fiscalYearStart"/>,
    /// computed in SQL (RAL-165 — perf audit Tier 1) — feeds the next ref-code sequence number
    /// without loading every LdipRecord to count in memory.
    /// </summary>
    Task<int> CountByFiscalYearStartAsync(int fiscalYearStart, CancellationToken ct = default);
}
