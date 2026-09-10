using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for the AIP division-allocation reservation ledger (V18-45 / PPDO-55).
/// Mirrors <see cref="IWfpAllocationLedgerRepository"/>, keyed per <b>activity</b> rather than per
/// record — see <see cref="AipDivisionAllocationLedger"/> for why.
///
/// Every query pushes its WHERE to SQL. The full ledger is never materialised in memory to find or
/// sum one division+FY's rows (<c>docs/PERFORMANCE_GUIDELINES.md</c>).
///
/// ⚠️ Nothing here clamps a sum at zero. Callers computing "remaining" subtract these figures from
/// the current allocation and may legitimately get a negative — that negative is the signal a
/// ceiling was cut below encoded work, and it is what blocks submit.
/// </summary>
public interface IAipAllocationLedgerRepository : IRepository<AipDivisionAllocationLedger>
{
    /// <summary>
    /// The ledger row for the unique (divisionId, fiscalYear, fundingSourceId, aipActivityId) key,
    /// or null.
    /// </summary>
    Task<AipDivisionAllocationLedger?> FindAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int aipActivityId, CancellationToken ct = default);

    /// <summary>
    /// Sum of <c>ReservedAmount</c> across every row for (divisionId, fiscalYear, fundingSourceId),
    /// optionally excluding one activity's row.
    ///
    /// The exclusion exists for the same reason as the WFP sibling's: when checking "would saving
    /// this activity exceed the remaining allocation", the activity being saved is excluded so its
    /// OLD reservation is not counted alongside its new, would-be one.
    /// </summary>
    Task<decimal> SumReservedAmountAsync(
        int divisionId, int fiscalYear, int fundingSourceId, int? excludeAipActivityId, CancellationToken ct = default);

    /// <summary>
    /// Distinct <c>FundingSourceId</c> values currently tracked for one activity. Used by the
    /// upsert so a fund the activity no longer uses gets its row recomputed down to zero rather
    /// than left stale at its last positive amount — the same staleness bug RAL-154 fixed on the
    /// WFP side.
    /// </summary>
    Task<IReadOnlyList<int>> GetFundingSourceIdsForActivityAsync(
        int aipActivityId, CancellationToken ct = default);

    /// <summary>
    /// Sum of <c>ReservedAmount</c> grouped by (DivisionId, FundingSourceId) across ALL the given
    /// divisions for one fiscal year, in a single GROUP BY.
    ///
    /// ⚠️ Call this once with the full division list, never once per division inside a loop — that
    /// is the N+1 the WFP sibling was rewritten to remove (RAL-176).
    /// </summary>
    Task<IReadOnlyList<DivisionFundReservedAmountDto>> SumReservedAmountsByDivisionsAsync(
        IReadOnlyList<int> divisionIds, int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// Deletes every ledger row belonging to one activity, for use in the same transaction that
    /// deletes the activity.
    ///
    /// ⚠️ This exists because the activity FK is <c>NoAction</c>, not <c>Cascade</c> — SQL Server
    /// refuses a second cascade path into this table alongside the divisions and funding_sources
    /// FKs. A reservation whose activity is gone would otherwise overstate consumed allocation and
    /// block a submit for work that no longer exists.
    /// </summary>
    Task<int> DeleteByActivityAsync(int aipActivityId, CancellationToken ct = default);
}

/// <summary>One division+fund's total reserved amount for a fiscal year (V18-45).</summary>
public sealed record DivisionFundReservedAmountDto(int DivisionId, int FundingSourceId, decimal ReservedAmount);
