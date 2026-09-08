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
    /// One row per activity belonging to config office <paramref name="configOfficeId"/> anywhere
    /// in AIP record <paramref name="aipRecordId"/>, carrying that activity's MOOE and CO summed
    /// over its lines for <paramref name="fundingSourceId"/> only (V18-46).
    ///
    /// <para>
    /// ⚠️ <b>Scoped to the CONFIG office, across every one of its sub-office group rows — not to a
    /// single <c>AipOffice</c> row.</b> ↩️ It used to take an <c>aipOfficeId</c>, which made the
    /// ceiling see one group's work and none of the others'. The ceiling is an office-level bound
    /// (<c>AipSubmitService</c>: "checked once for the office, not per group"), and the caller was
    /// passing <c>Groups[0]</c>, so every group after the first was invisible to it: an office
    /// could encode unlimited General Fund money in a second block and submit cleanly. PGO has
    /// eight group rows and the entire ceiling was computed from one of them. Found by
    /// live-testing after the LDIP sub-office fix multiplied the number of groups per office.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Returns per-activity figures, deliberately un-summed.</b> The ceiling rule rounds each
    /// printed figure UP to the thousand and only then adds (DECISION 9), so the caller must see
    /// the individual figures. Returning a single total here would force the rounding to happen
    /// after the sum, which is a different — and smaller — number than the form prints.
    /// </para>
    ///
    /// <para>
    /// ⚠️ PS is not returned at all. It is exempt from the ceiling as an expense class
    /// (tracker A6-2), and returning it invites a caller to add it in.
    /// </para>
    ///
    /// Activities with no lines for this fund are omitted rather than returned as zero rows;
    /// a zero contributes nothing to a sum either way.
    /// </summary>
    Task<IReadOnlyList<AipActivityFundTotalsDto>> SumMooeCoByConfigOfficeAndFundAsync(
        int aipRecordId, int configOfficeId, int fundingSourceId, CancellationToken ct = default);

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

    /// <summary>
    /// The distinct funding-source codes each activity in one AIP record draws on, in the order
    /// they were first used (PPDO-80).
    ///
    /// ⚠️ <b>The activity row cannot answer this itself.</b> On an entered year the fund lives on
    /// the expenditure LINE — one fund per line, Phase 2 decision 4 — so an activity drawing on
    /// two funds has no single fund to store, and <c>AipActivity.FundingSourceSnapshot</c> stays
    /// null. The tree renders collapsed activities without loading their lines, so without this
    /// the form's Funding Source column (7) could not be shown at all.
    ///
    /// ⚠️ Scoped by RECORD, not by an id list. The tree read hands out every activity in the
    /// record for a host-office caller, and passing thousands of ids through <c>Contains</c> is
    /// the shape this avoids; the join down office → program → project → activity costs nothing
    /// extra, and <see cref="SumMooeCoByConfigOfficeAndFundAsync"/> already takes it.
    ///
    /// ⚠️ Reads <c>FundingSourceSnapshot</c>, not a join to the config table — the code a line was
    /// entered under is what the form must print, even after somebody renames the fund.
    ///
    /// Lines naming no fund are omitted. An activity with no funded line produces no rows at all
    /// rather than an empty entry, so a caller must treat an absent id as "no funds".
    /// </summary>
    Task<IReadOnlyList<AipActivityFundCodeDto>> GetFundCodesByAipRecordAsync(
        int aipRecordId, CancellationToken ct = default);

    /// <summary>
    /// The single-activity form of <see cref="GetFundCodesByAipRecordAsync"/>, already joined into
    /// display order.
    ///
    /// Exists because both single-activity writes — an expenditure line, and the details save —
    /// return the activity for the page to splice into its tree WITHOUT reloading. Leaving the
    /// codes off either one blanks the fund cell the moment an encoder edits the row.
    /// </summary>
    Task<IReadOnlyList<string>> GetFundCodesByActivityIdAsync(
        int activityId, CancellationToken ct = default);

    // ── Procurement items (V18-80 / PPDO-54) ──────────────────────────────────

    /// <summary>
    /// The procurement items belonging to a set of expenditure lines, in one query.
    ///
    /// ⚠️ Bulk by design. The entry table renders every line of an activity with its items, so the
    /// per-line form of this would be one query per line — the N+1 that
    /// <see cref="GetByActivityIdsAsync"/> exists to avoid one level up. It is a separate call
    /// rather than an <c>Include</c> on the line reads because the ledger upsert and the ceiling
    /// sum read those same lines and need no items; fattening them would make every caller pay.
    ///
    /// Ordered by expenditure then id, so a rendered list is stable between loads.
    /// </summary>
    Task<IReadOnlyList<AipProcurementItem>> GetProcurementItemsByExpenditureIdsAsync(
        IReadOnlyList<int> expenditureIds, CancellationToken ct = default);

    /// <summary>
    /// Replaces one line's procurement items wholesale — deletes what is there, adds
    /// <paramref name="items"/>. Does <b>not</b> save; the calling service owns the unit of work,
    /// as everywhere else here.
    ///
    /// ⚠️ <b>The one write method on this interface</b>, against the note below, and for a reason
    /// the note's argument does not cover: replacing a set is not an Add, an Update or a Delete of
    /// a single entity, and expressing it through the generic repository would mean the service
    /// loading every existing row purely to hand each one back for deletion. Keeping the
    /// delete-then-insert in one place is also what stops a caller inserting the new items and
    /// forgetting the old ones, which is a silent doubling of the line's cost.
    /// </summary>
    Task ReplaceProcurementItemsAsync(
        int expenditureId, IReadOnlyList<AipProcurementItem> items, CancellationToken ct = default);

    // ── No other write methods here, deliberately ─────────────────────────────
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

/// <summary>
/// One funding-source code used by one activity, with the id of the earliest line that used it
/// (PPDO-80). Follows the repository-projection convention — declared beside its interface, not in
/// Application.
///
/// ⚠️ <see cref="FirstLineId"/> is an ORDERING key, not data for display. SQL cannot build the
/// joined <c>GF/GAD Fund</c> string, so the codes come back one per row and the caller joins them;
/// this is what makes that join reproduce the order the encoder entered the funds in. Sorting the
/// codes alphabetically instead would silently reorder a printed cell between two loads of the
/// same unchanged data.
/// </summary>
public sealed record AipActivityFundCodeDto(int ActivityId, string Code, int FirstLineId);
