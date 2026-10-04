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

    /// <summary>
    /// The submitted rows for several offices of one AIP record, <b>untracked</b> — the read side of
    /// the division lock (PPDO-148). One query for a whole tree, never one per office.
    /// </summary>
    Task<IReadOnlyList<AipDivisionSubmission>> GetForOfficesAsync(
        int aipRecordId, IReadOnlyList<int> officeIds, CancellationToken ct = default);

    /// <summary>
    /// Every division of the given config offices, <b>active or not</b>, untracked (PPDO-148).
    /// Inactive ones are returned so an activity still tagged with one can show its name; callers
    /// decide "does this office have divisions" from the active ones only.
    /// </summary>
    Task<IReadOnlyList<Division>> GetDivisionsByOfficeIdsAsync(
        IReadOnlyList<int> officeIds, CancellationToken ct = default);

    /// <summary>One division by id, untracked, active or not — null when there is none (PPDO-149).</summary>
    Task<Division?> GetDivisionAsync(int divisionId, CancellationToken ct = default);

    /// <summary>Stages a new row. It is written by <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(AipDivisionSubmission submission, CancellationToken ct = default);

    /// <summary>
    /// Saves every pending change on the shared context — the division row AND the office's
    /// workflow state, so a transition commits as one (PPDO-149). ⚠️ Throws
    /// <see cref="PPDO.Domain.Common.UniqueConstraintViolationException"/> when the
    /// (aip_record_id, division_id) index rejects a second first-submit of the same division.
    /// </summary>
    Task SaveChangesAsync(CancellationToken ct = default);
}
