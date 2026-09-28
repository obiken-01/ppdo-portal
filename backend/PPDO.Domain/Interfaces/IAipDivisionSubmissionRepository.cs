using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository for <see cref="AipDivisionSubmission"/> (PPDO-130). Not an <see cref="IRepository{T}"/>:
/// the table is only ever read one office at a time, and nothing lists it across offices.
/// </summary>
public interface IAipDivisionSubmissionRepository
{
    /// <summary>
    /// Every submission row for one office in one AIP record, <b>tracked</b> so a transition can
    /// update it in place. One query per office, never one per division.
    ///
    /// ⚠️ A division with no row is in Draft, so this list is not "every division of the
    /// office". Callers join it against the office's divisions and treat a missing row as Draft.
    /// </summary>
    Task<IReadOnlyList<AipDivisionSubmission>> GetForOfficeAsync(
        int aipRecordId, int officeId, CancellationToken ct = default);

    /// <summary>Stages a new row. It is written by <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(AipDivisionSubmission submission, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
