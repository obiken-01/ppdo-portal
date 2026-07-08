using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped, server-side WFP expenditure reads (v1.4 WFP Rework —
/// RAL-120). Mirrors <see cref="IWfpRepository"/>'s pattern: all query methods push WHERE
/// filters to SQL so the full wfp_expenditures/wfp_expenditure_periods/wfp_procurement_items
/// tables are never loaded in memory just to read one expenditure's children.
/// </summary>
public interface IWfpExpenditureRepository : IRepository<WfpExpenditure>
{
    /// <summary>
    /// Returns the expenditure whose integer PK equals <paramref name="id"/>, or null.
    /// Needed because the base <see cref="IRepository{T}.GetByIdAsync"/> uses a Guid key.
    /// </summary>
    Task<WfpExpenditure?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>WfpExpenditurePeriod rows WHERE expenditure_id = <paramref name="expenditureId"/>, ordered by period_no.</summary>
    Task<IReadOnlyList<WfpExpenditurePeriod>> GetPeriodsByExpenditureIdAsync(int expenditureId, CancellationToken ct = default);

    /// <summary>WfpProcurementItem rows WHERE expenditure_id = <paramref name="expenditureId"/>, ordered by period_no.</summary>
    Task<IReadOnlyList<WfpProcurementItem>> GetProcurementItemsByExpenditureIdAsync(int expenditureId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the WFP record/office/division/AIP-activity context for a given expenditure's
    /// parent activity — needed by the ceiling checks (RAL-122), which only ever start from a
    /// wfp_activity_id. Returns null if the activity doesn't exist.
    /// </summary>
    Task<WfpExpenditureContext?> GetActivityContextAsync(int wfpActivityId, CancellationToken ct = default);

    /// <summary>
    /// Sum of TotalAppropriation across every wfp_expenditure whose parent activity references
    /// <paramref name="aipActivityId"/>, scoped to the given office+fiscal year — i.e. across
    /// ALL divisions of the office (§8's AIP budget check). Computed in SQL, never in memory.
    /// </summary>
    Task<decimal> SumTotalByAipActivityAsync(
        int aipActivityId, int officeId, int fiscalYear,
        int? excludeExpenditureId, CancellationToken ct = default);

    /// <summary>
    /// Sum of TotalAppropriation across every wfp_expenditure under the given WFP record
    /// (all of its activities) — the record-level total the division-allocation ledger tracks.
    /// </summary>
    Task<decimal> SumTotalByWfpRecordAsync(
        int wfpRecordId, int? excludeExpenditureId, CancellationToken ct = default);
}

/// <summary>
/// Slim projection of a wfp_activity's parent chain, resolved in one query so ceiling checks
/// (RAL-122) don't need separate round-trips to wfp_records/wfp_activities.
/// </summary>
public sealed record WfpExpenditureContext(
    int WfpRecordId, int? DivisionId, int OfficeId, int FiscalYear, int AipActivityId);
