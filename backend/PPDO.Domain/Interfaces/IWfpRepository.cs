using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped, server-side WFP reads.
/// All query methods push WHERE / IN / TOP filters to SQL so the full wfp_records,
/// wfp_activities, and wfp_expenditure_lines tables are never loaded in memory
/// just to read or locate one record.
/// </summary>
public interface IWfpRepository : IRepository<WfpRecord>
{
    /// <summary>
    /// Returns the WFP record whose integer PK equals <paramref name="id"/>, or null.
    /// Needed because the base <see cref="IRepository{T}.GetByIdAsync"/> uses a Guid key.
    /// </summary>
    Task<WfpRecord?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Returns WFP records filtered by <paramref name="aipRecordId"/>, <paramref name="officeId"/>,
    /// and/or <paramref name="divisionId"/> entirely in SQL, ordered by UpdatedAt descending.
    /// Null parameters mean "no filter on that column".
    /// </summary>
    Task<IReadOnlyList<WfpRecord>> GetFilteredAsync(
        int? aipRecordId, int? officeId, int? divisionId = null, CancellationToken ct = default);

    /// <summary>
    /// Finds the single WFP record for the given (AipRecordId, OfficeId, DivisionId) triplet, or null.
    /// Used by SaveAsync to detect whether this is a create or an update.
    /// A null <paramref name="divisionId"/> matches rows WHERE division_id IS NULL.
    /// </summary>
    Task<WfpRecord?> FindByAipOfficeAndDivisionAsync(
        int aipRecordId, int officeId, int? divisionId, CancellationToken ct = default);

    /// <summary>
    /// Finds the single WFP record for the given (OfficeId, DivisionId, FiscalYear) triplet, or
    /// null. Unlike <see cref="FindByAipOfficeAndDivisionAsync"/> this resolves directly off the
    /// denormalized FiscalYear column on WfpRecord, without needing the caller to first look up
    /// an AipRecordId — used by the scoped cleanup endpoint (RAL-137).
    /// A null <paramref name="divisionId"/> matches rows WHERE division_id IS NULL.
    /// </summary>
    Task<WfpRecord?> FindByOfficeDivisionFiscalYearAsync(
        int officeId, int? divisionId, int fiscalYear, CancellationToken ct = default);

    /// <summary>WfpActivity rows WHERE wfp_id = <paramref name="wfpId"/>.</summary>
    Task<IReadOnlyList<WfpActivity>> GetActivitiesByWfpIdAsync(int wfpId, CancellationToken ct = default);

    /// <summary>
    /// Batched sibling of <see cref="GetActivitiesByWfpIdAsync"/> — WfpActivity rows WHERE
    /// wfp_id IN (<paramref name="wfpIds"/>), ordered by wfp_id then aip_activity_id. One query
    /// instead of one-per-record; the WFP report builds its activity map this way across every
    /// division's WFP record (v1.4.3 — RAL-158). Empty input returns empty without hitting the DB.
    /// </summary>
    Task<IReadOnlyList<WfpActivity>> GetActivitiesByWfpIdsAsync(IReadOnlyList<int> wfpIds, CancellationToken ct = default);

    /// <summary>WfpExpenditureLine rows WHERE wfp_activity_id IN (<paramref name="activityIds"/>).</summary>
    Task<IReadOnlyList<WfpExpenditureLine>> GetLinesByActivityIdsAsync(IReadOnlyList<int> activityIds, CancellationToken ct = default);
}
