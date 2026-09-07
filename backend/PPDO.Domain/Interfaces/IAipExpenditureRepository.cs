using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="AipExpenditure"/> (v1.8.0 Phase 2 — V18-33).
///
/// Every method scopes in SQL. <see cref="AipExpenditure"/> is a leaf table that will grow with one
/// row per costed line per activity per office per fiscal year, so nothing here may load it whole —
/// see <c>docs/PERFORMANCE_GUIDELINES.md</c>.
///
/// <b>The base <see cref="IRepository{T}"/> is Guid-keyed</b> and this entity has an int PK, which
/// is why <see cref="GetByIntIdAsync"/> exists — the same reason <c>IAipRepository</c> has one.
/// </summary>
public interface IAipExpenditureRepository : IRepository<AipExpenditure>
{
    /// <summary>Returns the expenditure whose integer PK equals <paramref name="id"/>, or null.</summary>
    Task<AipExpenditure?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Every expenditure line for one activity, ordered by id so a rendered list is stable between
    /// loads. The read this table exists for.
    /// </summary>
    Task<IReadOnlyList<AipExpenditure>> GetByActivityIdAsync(int activityId, CancellationToken ct = default);

    /// <summary>
    /// Lines for several activities at once — the batch form of
    /// <see cref="GetByActivityIdAsync"/>. Exists so a caller rendering a whole AIP does not fire
    /// one query per activity; that N+1 is the shape <c>GetAllocationsByFundAsync</c> was rewritten
    /// to avoid after it cost ~60 sequential round trips on the dashboard (RAL-166).
    /// </summary>
    Task<IReadOnlyList<AipExpenditure>> GetByActivityIdsAsync(
        IReadOnlyList<int> activityIds, CancellationToken ct = default);

    /// <summary>
    /// The activity's PS / MOOE / CO / Total summed in SQL — one small row, no entities
    /// transferred. This is what V18-34's recompute will read; it is here rather than there so the
    /// aggregate never becomes a <c>GetByActivityIdAsync(...).Sum(...)</c> in memory.
    /// </summary>
    Task<AipExpenditureTotalsDto> SumByActivityIdAsync(int activityId, CancellationToken ct = default);

    /// <summary>
    /// One row per activity under <paramref name="aipOfficeId"/>, carrying that activity's MOOE
    /// and CO summed over its lines for <paramref name="fundingSourceId"/> only (V18-46).
    ///
    /// ⚠️ <b>Returns per-activity figures, deliberately un-summed.</b> The ceiling rule rounds each
    /// printed figure UP to the thousand and only then adds (DECISION 9), so the caller must see
    /// the individual figures. Returning a single total here would force the rounding to happen
    /// after the sum, which is a different — and smaller — number than the form prints.
    ///
    /// ⚠️ PS is not returned at all. It is exempt from the ceiling as an expense class
    /// (tracker A6-2), and returning it invites a caller to add it in.
    ///
    /// Activities with no lines for this fund are omitted rather than returned as zero rows;
    /// a zero contributes nothing to a sum either way.
    /// </summary>
    Task<IReadOnlyList<AipActivityFundTotalsDto>> SumMooeCoByOfficeAndFundAsync(
        int aipOfficeId, int fundingSourceId, CancellationToken ct = default);

    /// <summary>
    /// Line counts for a set of activities, computed in SQL (V18-49 / PPDO-59).
    ///
    /// ⚠️ Counts, not rows. The submit checklist asks only "does this activity have any lines?" for
    /// every activity in an office — loading the lines themselves to count them would pull an
    /// office's entire expenditure table into memory to answer a yes/no question per activity.
    ///
    /// ⚠️ Activities with no lines are <b>omitted</b>, not returned as zero. The caller must treat
    /// an absent id as zero, which it has to do anyway: a SUM/COUNT over no rows produces no group.
    /// </summary>
    Task<IReadOnlyList<AipActivityLineCountDto>> CountByActivityIdsAsync(
        IReadOnlyList<int> activityIds, CancellationToken ct = default);

    // ── No write methods here, deliberately ───────────────────────────────────
    // Writes go through the base IRepository<T>'s Add/Update/Delete, with the calling Application
    // service owning SaveChangesAsync — the unit-of-work rule stated on Repository<T> and followed
    // by every feature repository in the project (IWfpExpenditureRepository is read-only too).
    //
    // Total's integrity does NOT depend on remembering to call a particular repository method:
    // AipExpenditure.Total has a private setter and can only be set through Recalculate(). That is
    // enforced by the type rather than by convention, which is the stronger guarantee and the
    // reason no AddLineAsync appears here.
}

/// <summary>
/// One activity's expenditure totals, computed in SQL (v1.8.0 Phase 2 — V18-33). Follows the
/// repository-projection convention used by <c>WfpActivityCoverageDto</c> and
/// <c>DivisionFundUsedAmountDto</c>: a record declared beside its interface, not an Application DTO.
///
/// All four are pesos, and all four are 0 — never null — when the activity has no lines. That
/// matters: <c>SUM</c> over no rows is SQL NULL, so the implementation must coalesce, or "no lines"
/// would be indistinguishable from "never computed" at the one place V18-34 reads.
/// </summary>
public sealed record AipExpenditureTotalsDto(
    decimal Ps,
    decimal Mooe,
    decimal Co,
    decimal Total,
    int LineCount);

/// <summary>
/// One activity's MOOE and CO for a single funding source, computed in SQL (V18-46 / PPDO-56).
/// Pesos, and <b>base</b> figures — the +30% uplift is presentation-only and never reaches here.
///
/// Deliberately carries no PS and no Total: PS is exempt from the ceiling check as an expense
/// class, and a Total would be the sum of an exempt and a non-exempt component.
/// </summary>
public sealed record AipActivityFundTotalsDto(
    int     ActivityId,
    decimal Mooe,
    decimal Co);

/// <summary>
/// How many expenditure lines one activity has, and how many of them name no funding source
/// (V18-49). Absent from the result means zero lines.
///
/// ⚠️ <see cref="LinesWithoutFund"/> exists because a fundless line is <b>invisible to the ceiling
/// check</b>: that check sums General Fund only, and a null fund is not the General Fund. Without
/// this an office could encode ₱50M against no fund, pass "has at least one line", contribute
/// nothing to its ceiling and submit cleanly. Found by live-testing.
/// </summary>
public sealed record AipActivityLineCountDto(int ActivityId, int LineCount, int LinesWithoutFund);
