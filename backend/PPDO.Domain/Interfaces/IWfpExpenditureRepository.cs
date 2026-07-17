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
    /// WfpExpenditure rows WHERE wfp_activity_id = <paramref name="wfpActivityId"/>, ordered by
    /// Id — the entry wizard's "expenditures added so far under this activity" list (RAL-123).
    /// </summary>
    Task<IReadOnlyList<WfpExpenditure>> GetByWfpActivityIdAsync(int wfpActivityId, CancellationToken ct = default);

    /// <summary>
    /// Batched sibling of <see cref="GetByWfpActivityIdAsync"/> — WfpExpenditure rows WHERE
    /// wfp_activity_id IN (<paramref name="wfpActivityIds"/>), ordered by wfp_activity_id then Id.
    /// One query instead of one-per-activity; the WFP report reads every division's expenditures
    /// this way (v1.4.3 — RAL-158). Empty input returns an empty list without hitting the DB.
    /// </summary>
    Task<IReadOnlyList<WfpExpenditure>> GetByWfpActivityIdsAsync(IReadOnlyList<int> wfpActivityIds, CancellationToken ct = default);

    /// <summary>
    /// Batched sibling of <see cref="GetPeriodsByExpenditureIdAsync"/> — WfpExpenditurePeriod rows
    /// WHERE expenditure_id IN (<paramref name="expenditureIds"/>), ordered by expenditure_id then
    /// period_no. The caller groups the flat result by ExpenditureId (v1.4.3 — RAL-158).
    /// </summary>
    Task<IReadOnlyList<WfpExpenditurePeriod>> GetPeriodsByExpenditureIdsAsync(IReadOnlyList<int> expenditureIds, CancellationToken ct = default);

    /// <summary>
    /// Batched sibling of <see cref="GetProcurementItemsByExpenditureIdAsync"/> — WfpProcurementItem
    /// rows WHERE expenditure_id IN (<paramref name="expenditureIds"/>), ordered by expenditure_id
    /// then period_no. The caller groups the flat result by ExpenditureId (v1.4.3 — RAL-158).
    /// </summary>
    Task<IReadOnlyList<WfpProcurementItem>> GetProcurementItemsByExpenditureIdsAsync(IReadOnlyList<int> expenditureIds, CancellationToken ct = default);

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
    /// (all of its activities) whose EFFECTIVE funding source is <paramref name="fundingSourceId"/>
    /// — where "effective" means <c>FundingSourceId ?? generalFundId</c> (an expenditure saved
    /// with no fund selected counts toward General Fund). This is the per-fund total the
    /// division-allocation ledger tracks (v1.4.3 — RAL-154). <paramref name="generalFundId"/>
    /// must be resolved by the caller — passing the wrong value here would silently misattribute
    /// every null-fund expenditure to whatever fund happens to be queried.
    /// </summary>
    Task<decimal> SumTotalByWfpRecordAsync(
        int wfpRecordId, int fundingSourceId, int generalFundId,
        int? excludeExpenditureId, CancellationToken ct = default);

    /// <summary>
    /// Distinct FundingSourceId values (including null, for expenditures with none selected)
    /// among a WFP record's expenditures (v1.4.3 — RAL-154). The caller coalesces null to
    /// General Fund before using these as ledger keys.
    /// </summary>
    Task<IReadOnlyList<int?>> GetDistinctFundingSourceIdsByWfpRecordAsync(
        int wfpRecordId, CancellationToken ct = default);

    /// <summary>
    /// WFP-activity coverage for the Dashboard's "activities with WFP expenditures" stat
    /// (v1.4.5 — RAL-161): the total WfpActivity count and how many of those have at least
    /// one WfpExpenditure, both scoped to (officeId, fiscalYear) and optionally one division.
    /// <paramref name="divisionId"/> null means every division of the office. Both counts are
    /// computed in SQL — the activity/expenditure tables are never loaded in memory.
    /// </summary>
    Task<WfpActivityCoverageDto> GetActivityCoverageAsync(
        int officeId, int? divisionId, int fiscalYear, CancellationToken ct = default);
}

/// <summary>Total WfpActivity rows vs. how many have at least one WfpExpenditure, for one scope.</summary>
public sealed record WfpActivityCoverageDto(int ActivitiesWithExpenditures, int TotalActivities);

/// <summary>
/// Slim projection of a wfp_activity's parent chain, resolved in one query so ceiling checks
/// (RAL-122) don't need separate round-trips to wfp_records/wfp_activities.
/// </summary>
public sealed record WfpExpenditureContext(
    int WfpRecordId, int? DivisionId, int OfficeId, int FiscalYear, int AipActivityId);
