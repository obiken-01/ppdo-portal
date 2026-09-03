namespace PPDO.Application.Services;

/// <summary>
/// Keeps an <c>aip_activities</c> row's stored <c>Ps</c>/<c>Mooe</c>/<c>Co</c>/<c>Total</c> equal to
/// the sum of its <c>aip_expenditures</c> lines (v1.8.0 Phase 2 — V18-34).
///
/// <para>
/// The totals stay <b>stored</b> rather than being summed on read. Every report, the WFP ceiling
/// check, the Budget Planning dashboard and the external API all read
/// <see cref="PPDO.Domain.Entities.AipActivity.Total"/>; recomputing on read would put a
/// <c>GROUP BY</c> underneath a report path, which <c>docs/PERFORMANCE_GUIDELINES.md</c> rules out
/// explicitly.
/// </para>
///
/// <para>
/// This is a seam, not a feature. Nothing writes expenditure lines yet — that is Phase 3 (V18-42),
/// which calls one of these two methods after every insert, update and delete of a line.
/// </para>
///
/// <para>
/// ⚠️ <b>Choose the method by what you just did, not by what looks convenient.</b> The two differ
/// only for an activity that ends up with no lines, and there they do opposite things — see each
/// method. Both stage and save in one unit of work.
/// </para>
/// </summary>
public interface IAipActivityTotalsService
{
    /// <summary>
    /// Recomputes after a line was <b>added or edited</b>, or as a defensive/bulk pass.
    ///
    /// <para>
    /// An activity with no lines is left exactly as it is. That is what keeps a bulk recompute from
    /// writing ₱0 over every FY≤2027 activity — they were imported from the province's workbook and
    /// have no child rows at all.
    /// </para>
    /// </summary>
    /// <returns>True if the activity's totals were changed.</returns>
    Task<bool> RecalculateAsync(int activityId, CancellationToken ct = default);

    /// <summary>
    /// Recomputes after a line was <b>deleted</b>. Identical to
    /// <see cref="RecalculateAsync"/> except that removing the activity's last line takes its totals
    /// to <b>0</b> rather than leaving them stale.
    ///
    /// <para>
    /// 0, never null. Null meant "never computed", which stops being a state that exists once an
    /// activity has had lines at all.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Only correct when the caller has just deleted a line from this activity, because that is
    /// the only way to know a no-lines activity is a costed one emptied rather than an imported one
    /// that never had children.
    /// </para>
    /// </summary>
    /// <returns>True if the activity's totals were changed.</returns>
    Task<bool> RecalculateAfterLineDeleteAsync(int activityId, CancellationToken ct = default);
}
