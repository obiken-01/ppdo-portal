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
    /// Writes <paramref name="totals"/> — summed from the activity's <c>aip_expenditures</c> rows by
    /// <see cref="IAipExpenditureRepository.SumByActivityIdAsync"/> — onto the parent activity's
    /// stored <c>Ps</c>/<c>Mooe</c>/<c>Co</c>/<c>Total</c> (v1.8.0 Phase 2 — V18-34).
    ///
    /// <para>
    /// ⚠️ <b>An activity with zero expenditure lines is ambiguous, and the two readings are
    /// opposites.</b> It is either an FY≤2027 activity imported from the province's workbook, which
    /// has never had child rows and whose figures must survive untouched — or an activity whose
    /// last line was just deleted, which must fall to 0. Both present as
    /// <see cref="AipExpenditureTotalsDto.LineCount"/> 0 with zero amounts; a SUM over no rows and a
    /// SUM of zeroes are indistinguishable, so the data cannot settle it.
    /// </para>
    ///
    /// <para>
    /// Only the caller knows which it is, so it says so via <paramref name="zeroWhenNoLines"/>,
    /// which defaults to the safe reading. Getting this wrong in the permissive direction wipes a
    /// fiscal year of historical figures silently.
    /// </para>
    ///
    /// <para>
    /// Follows the unit-of-work rule the rest of the project uses: this stages the change, the
    /// calling Application service owns <c>SaveChangesAsync</c>.
    /// </para>
    /// </summary>
    /// <param name="zeroWhenNoLines">
    /// <c>false</c> (default) — leave an activity with no lines alone. Correct for any recompute
    /// that was not caused by a write to this activity's own lines, including a bulk pass, because
    /// it cannot wipe an imported activity.
    /// <c>true</c> — write the zeroes. Correct <b>only</b> when the caller has just deleted a line
    /// from this activity and therefore knows it was expenditure-derived a moment ago.
    /// </param>
    /// <returns>True when the parent was updated; false when it was deliberately left alone.</returns>
    Task<bool> ApplyActivityTotalsAsync(
        int activityId,
        AipExpenditureTotalsDto totals,
        bool zeroWhenNoLines = false,
        CancellationToken ct = default);

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

    /// <summary>
    /// One page of the AIP Review search (v1.8.0 Phase 4 — V18-75 / PPDO-76,
    /// <c>AIP_Review_Spec.md</c> §4.1), plus the chip counts the filter panel renders.
    ///
    /// <para>
    /// <b>⚠️ Every filter is applied in SQL, and that is the whole point of this method.</b> The
    /// obvious implementation — load the record's offices, programs, projects and activities and
    /// filter the tree in memory — passes every test on local dev data and falls over on the real
    /// record, which is thousands of nodes across nineteen offices. It is also the exact pattern
    /// <c>docs/PERFORMANCE_GUIDELINES.md</c> exists to prevent.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Scope is the caller's job, not this method's.</b> Pass an already-resolved
    /// <see cref="AipReviewSearchQuery.OfficeIds"/>; the repository applies whatever it is given.
    /// Putting <c>OfficeScope</c> down here would hide the security decision inside a query.
    /// </para>
    /// </summary>
    Task<AipReviewSearchPage> SearchReviewNodesAsync(
        AipReviewSearchQuery query, CancellationToken ct = default);
}

/// <summary>
/// The parsed, already-scoped filter set for one AIP Review search (PPDO-76).
///
/// <para>
/// ⚠️ <b>Everything here is a parsed value, never raw user input.</b> The OR-list splitting and the
/// scope clamp both happen in the Application layer; by the time a query reaches the repository the
/// only question left is which rows match.
/// </para>
/// </summary>
/// <param name="OfficeIds">
/// Config office ids to include. ⚠️ <b>Empty means "no office filter", not "no offices"</b> — a
/// caller that means "nothing" must not call at all. A clamped guest office passes exactly one id.
/// </param>
/// <param name="RefCodePrefixes">
/// Zero or more <b>prefixes</b>, OR-ed together. ⚠️ Matched as <c>LIKE 'x%'</c> — never
/// <c>LIKE '%x%'</c>, which cannot use an index and is what the spec forbids by name.
/// </param>
/// <param name="Title">
/// Free text over the node name, matched as a substring. ⚠️ <b>A single value, never OR-split</b> —
/// a project title may legitimately contain the word "or".
/// </param>
public sealed record AipReviewSearchQuery(
    int                 AipRecordId,
    IReadOnlyList<int>  OfficeIds,
    IReadOnlyList<string> Sectors,
    IReadOnlyList<string> WorkflowStatuses,
    IReadOnlyList<string> RefCodePrefixes,
    string?             Title,
    int                 Skip,
    int                 Take);

/// <summary>
/// One matching node — a program, project or activity — as the results grid renders it (PPDO-76).
///
/// <para>
/// ⚠️ <b>Slim on purpose.</b> No amounts, no expected outputs, no descriptions: the grid shows none
/// of them, and a fat AIP DTO once produced a 1.2 MB response
/// (<c>docs/PERFORMANCE_GUIDELINES.md</c>). A reviewer who wants the figures opens the row.
/// </para>
/// </summary>
/// <param name="Level"><c>Program</c>, <c>Project</c> or <c>Activity</c>.</param>
/// <param name="OfficeId">
/// The <b>config</b> office that owns this node's group — what the result row links on. Null only
/// for a legacy row the V18-32 backfill could not match.
/// </param>
public sealed record AipReviewNodeRow(
    string  Level,
    int     NodeId,
    string  RefCode,
    string  Name,
    int     AipOfficeId,
    string  AipOfficeName,
    int?    OfficeId,
    string  Sector,
    string  WorkflowStatus);

/// <summary>
/// A page of search results with its chip counts (PPDO-76).
///
/// <para>
/// ⚠️ <b>Each count set is computed with its own field's filter removed</b> — standard facet
/// behaviour. Counting with every filter applied would make a selected chip show its own total and
/// every sibling show zero, which turns "Select multiple to combine" into a lie.
/// </para>
/// </summary>
public sealed record AipReviewSearchPage(
    IReadOnlyList<AipReviewNodeRow> Items,
    int                             TotalCount,
    IReadOnlyDictionary<string,int> SectorCounts,
    IReadOnlyDictionary<string,int> WorkflowStatusCounts);

/// <summary>
/// Activity counts and money for one AipOffice row (PPDO-20). "Costed" means the activity has a
/// non-null, non-zero <see cref="AipActivity.Total"/> — money has actually been entered against
/// it, which is what the dashboard reports on now that WFP expenditure coverage is gone from the
/// page (Budget_Planning_Dashboard_Requirements.md §2, decisions 3 and 4).
/// </summary>
public sealed record AipOfficeRollupDto(
    int     AipOfficeId,
    string  RefCode,
    // The config office that owns this AIP office (V18-32), or null for an unmatched legacy row.
    // Scoping matches on this; RefCode is carried for display and re-linking only.
    int?    OfficeId,
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
