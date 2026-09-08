using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="AipReviewComment"/> (v1.8.0 Phase 4 — V18-53 / PPDO-71).
///
/// Every method scopes in SQL. The table grows with one row per remark per row per office per
/// fiscal year, so nothing here loads it whole (<c>docs/PERFORMANCE_GUIDELINES.md</c>).
///
/// <b>The base <see cref="IRepository{T}"/> is Guid-keyed</b> and this entity has an int PK, hence
/// <see cref="GetByIntIdAsync"/> — the same reason <c>IAipRepository</c> and
/// <c>IAipExpenditureRepository</c> each have one.
/// </summary>
public interface IAipReviewCommentRepository : IRepository<AipReviewComment>
{
    /// <summary>Returns the comment whose integer PK equals <paramref name="id"/>, or null.</summary>
    Task<AipReviewComment?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Every comment on the given office rows, resolved ones included, oldest first.
    ///
    /// ⚠️ Takes the <b>set</b> of an office's sub-office group rows, not one: an office holds one
    /// <c>AipOffice</c> row per group and a reviewer reads the office's comments, not one group's.
    ///
    /// ⚠️ Resolved comments are returned, not filtered out — they are marked, never deleted, and
    /// PPDO-77's history reads them back.
    /// </summary>
    Task<IReadOnlyList<AipReviewComment>> GetByOfficeIdsAsync(
        IReadOnlyList<int> aipOfficeIds, CancellationToken ct = default);

    /// <summary>
    /// The unresolved count per authoring side, computed in SQL.
    ///
    /// ⚠️ A <c>GROUP BY</c>, not a list fetched and counted in memory: this runs on every review
    /// page load and again on every re-submit, and it is the number the soft gate warns with
    /// (PPDO-72). Sides with no unresolved comments are absent from the result — the caller treats
    /// absent as zero rather than expecting two rows.
    /// </summary>
    Task<IReadOnlyDictionary<AipCommentSide, int>> CountUnresolvedBySideAsync(
        IReadOnlyList<int> aipOfficeIds, CancellationToken ct = default);
}
