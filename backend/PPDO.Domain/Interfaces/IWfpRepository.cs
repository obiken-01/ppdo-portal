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
    /// Returns WFP records filtered by <paramref name="aipRecordId"/> and/or
    /// <paramref name="officeId"/> entirely in SQL, ordered by UpdatedAt descending.
    /// Null parameters mean "no filter on that column".
    /// </summary>
    Task<IReadOnlyList<WfpRecord>> GetFilteredAsync(int? aipRecordId, int? officeId, CancellationToken ct = default);

    /// <summary>
    /// Finds the single WFP record for the given (AipRecordId, OfficeId) pair, or null.
    /// Used by SaveAsync to detect whether this is a create or an update.
    /// </summary>
    Task<WfpRecord?> FindByAipAndOfficeAsync(int aipRecordId, int officeId, CancellationToken ct = default);

    /// <summary>WfpActivity rows WHERE wfp_id = <paramref name="wfpId"/>.</summary>
    Task<IReadOnlyList<WfpActivity>> GetActivitiesByWfpIdAsync(int wfpId, CancellationToken ct = default);

    /// <summary>WfpExpenditureLine rows WHERE wfp_activity_id IN (<paramref name="activityIds"/>).</summary>
    Task<IReadOnlyList<WfpExpenditureLine>> GetLinesByActivityIdsAsync(IReadOnlyList<int> activityIds, CancellationToken ct = default);
}
