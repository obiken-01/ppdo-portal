using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped, server-side AIP hierarchy reads.
/// All query methods apply WHERE / IN filters in SQL — the full hierarchy tables
/// are never materialised in memory just to find one record.
/// </summary>
public interface IAipRepository : IRepository<AipRecord>
{
    /// <summary>
    /// Returns the AIP record whose integer PK equals <paramref name="id"/>, or null.
    /// Needed because the base <see cref="IRepository{T}.GetByIdAsync"/> uses a Guid key.
    /// </summary>
    Task<AipRecord?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>AipOffice rows WHERE aip_record_id = <paramref name="aipRecordId"/>.</summary>
    Task<IReadOnlyList<AipOffice>> GetOfficesByAipIdAsync(int aipRecordId, CancellationToken ct = default);

    /// <summary>AipOffice rows WHERE aip_record_id IN (<paramref name="aipIds"/>). Used by the list endpoint for office-count aggregation.</summary>
    Task<IReadOnlyList<AipOffice>> GetOfficesByAipIdsAsync(IReadOnlyList<int> aipIds, CancellationToken ct = default);

    /// <summary>AipProgram rows WHERE office_id IN (<paramref name="officeIds"/>).</summary>
    Task<IReadOnlyList<AipProgram>> GetProgramsByOfficeIdsAsync(IReadOnlyList<int> officeIds, CancellationToken ct = default);

    /// <summary>Returns the single AipProgram whose PK equals <paramref name="id"/>, or null (v1.4 Q1 function-band edit).</summary>
    Task<AipProgram?> GetProgramByIdAsync(int id, CancellationToken ct = default);

    /// <summary>AipProject rows WHERE program_id IN (<paramref name="programIds"/>).</summary>
    Task<IReadOnlyList<AipProject>> GetProjectsByProgramIdsAsync(IReadOnlyList<int> programIds, CancellationToken ct = default);

    /// <summary>AipActivity rows WHERE project_id IN (<paramref name="projectIds"/>).</summary>
    Task<IReadOnlyList<AipActivity>> GetActivitiesByProjectIdsAsync(IReadOnlyList<int> projectIds, CancellationToken ct = default);

    /// <summary>Returns the single AipActivity whose PK equals <paramref name="id"/>, or null (RAL-122 ceiling checks).</summary>
    Task<AipActivity?> GetActivityByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// The single non-Archived AipRecord for <paramref name="fiscalYear"/> (v1.4.5 — RAL-161).
    /// AIP records aren't office-scoped — one record spans every office via its AipOffice
    /// children — so this is a plain WHERE FiscalYear = @fy query, not filtered by office.
    /// Ordered by Id for determinism when more than one non-Archived record exists for the year.
    /// </summary>
    Task<AipRecord?> GetLatestByFiscalYearAsync(int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Distinct AipRecord.FiscalYear values, newest first (v1.4.5 — RAL-161) — the Dashboard's
    /// fiscal-year picker, computed in SQL instead of loading every AipRecord to dedupe in memory.
    /// </summary>
    Task<IReadOnlyList<int>> GetDistinctFiscalYearsAsync(CancellationToken ct = default);
}
