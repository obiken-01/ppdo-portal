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
