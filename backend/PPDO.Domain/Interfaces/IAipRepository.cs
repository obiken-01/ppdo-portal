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

    /// <summary>Returns the single AipOffice whose PK equals <paramref name="id"/>, or null (RAL-62 manual entry).</summary>
    Task<AipOffice?> GetOfficeByIdAsync(int id, CancellationToken ct = default);

    /// <summary>AipOffice rows WHERE aip_record_id IN (<paramref name="aipIds"/>). Used by the list endpoint for office-count aggregation.</summary>
    Task<IReadOnlyList<AipOffice>> GetOfficesByAipIdsAsync(IReadOnlyList<int> aipIds, CancellationToken ct = default);

    /// <summary>AipProgram rows WHERE office_id IN (<paramref name="officeIds"/>).</summary>
    Task<IReadOnlyList<AipProgram>> GetProgramsByOfficeIdsAsync(IReadOnlyList<int> officeIds, CancellationToken ct = default);

    /// <summary>Returns the single AipProgram whose PK equals <paramref name="id"/>, or null (v1.4 Q1 function-band edit).</summary>
    Task<AipProgram?> GetProgramByIdAsync(int id, CancellationToken ct = default);

    /// <summary>AipProject rows WHERE program_id IN (<paramref name="programIds"/>).</summary>
    Task<IReadOnlyList<AipProject>> GetProjectsByProgramIdsAsync(IReadOnlyList<int> programIds, CancellationToken ct = default);

    /// <summary>Returns the single AipProject whose PK equals <paramref name="id"/>, or null (RAL-62 manual entry).</summary>
    Task<AipProject?> GetProjectByIdAsync(int id, CancellationToken ct = default);

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

    /// <summary>
    /// One <see cref="AipOfficeRollupDto"/> per AipOffice row of <paramref name="aipRecordId"/>
    /// (PPDO-20): how many activities the office has, how many carry money, and what they cost.
    ///
    /// <b>Why this exists rather than reusing the four hierarchy reads.</b>
    /// <c>BuildOfficeAipSummaryAsync</c> walks offices → programs → projects → activities in four
    /// round trips for ONE office. The offices table on the dashboard needs the same figures for
    /// every office in scope, and repeating that walk would be fourteen offices × four queries,
    /// materialising every activity row in the AIP just to count and sum them. This does the
    /// GROUP BY in SQL and returns one small row per office
    /// (<c>docs/PERFORMANCE_GUIDELINES.md</c>).
    /// </summary>
    Task<IReadOnlyList<AipOfficeRollupDto>> GetOfficeRollupsAsync(
        int aipRecordId, CancellationToken ct = default);

    /// <summary>
    /// One <see cref="AipProgramRollupDto"/> per program under <paramref name="aipOfficeIds"/>
    /// (PPDO-20) — the same aggregate as <see cref="GetOfficeRollupsAsync"/> one level down.
    ///
    /// Keyed by <see cref="AipProgram.RefCode"/> rather than its surrogate id because that is what
    /// a division assignment matches on: <c>program_divisions</c> is deliberately ref-code-keyed
    /// and survives a re-upload, while <c>aip_programs.Id</c> does not (see
    /// <see cref="ProgramDivision"/>). Two program rows in one office can share a ref code only if
    /// the import is malformed; the rollup sums them, which is the right answer either way.
    /// </summary>
    Task<IReadOnlyList<AipProgramRollupDto>> GetProgramRollupsAsync(
        IReadOnlyList<int> aipOfficeIds, CancellationToken ct = default);
}

/// <summary>
/// Activity counts and money for one AipOffice row (PPDO-20). "Costed" means the activity has a
/// non-null, non-zero <see cref="AipActivity.Total"/> — money has actually been entered against
/// it, which is what the dashboard reports on now that WFP expenditure coverage is gone from the
/// page (Budget_Planning_Dashboard_Requirements.md §2, decisions 3 and 4).
/// </summary>
public sealed record AipOfficeRollupDto(
    int     AipOfficeId,
    string  RefCode,
    int     ActivityCount,
    int     CostedActivityCount,
    decimal CostedTotal);

/// <summary>
/// The same rollup one level down, per program ref code (PPDO-20). See
/// <see cref="IAipRepository.GetProgramRollupsAsync"/> for why the key is the ref code.
/// </summary>
public sealed record AipProgramRollupDto(
    int     AipOfficeId,
    string  ProgramRefCode,
    int     ActivityCount,
    int     CostedActivityCount,
    decimal CostedTotal);
