using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Data access for SAIPs and their draft copies (v1.10.0 — PPDO-246).
///
/// <para>
/// ⚠️ <b>The only door to draft copies.</b> <c>aip_offices</c> carries a global query filter
/// (<c>saip_id IS NULL</c>) so every other read skips copies. The methods here that touch copies use
/// <c>IgnoreQueryFilters()</c>, narrowed to one SAIP's rows. Nothing else in the codebase should.
/// </para>
/// </summary>
public interface ISaipRepository : IRepository<AipSaip>
{
    /// <summary>One SAIP by its id, tracked. Null when none.</summary>
    Task<AipSaip?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>Every SAIP of one office in one record, newest number first.</summary>
    Task<IReadOnlyList<AipSaip>> GetForOfficeAsync(int aipRecordId, int officeId, CancellationToken ct = default);

    /// <summary>
    /// The office's open SAIP (status not Approved/Cancelled), or null. There is at most one —
    /// the filtered unique index holds that.
    /// </summary>
    Task<AipSaip?> GetOpenAsync(int aipRecordId, int officeId, CancellationToken ct = default);

    /// <summary>The highest number used so far for this office and record, cancelled ones included; 0 when none.</summary>
    Task<int> GetMaxNumberAsync(int aipRecordId, int officeId, CancellationToken ct = default);

    /// <summary>
    /// Stages a draft copy for insert: the copied group rows (with their programs → projects →
    /// activities attached through the navigation collections) and the copied expenditure lines
    /// (each pointing at its copied activity through <see cref="AipExpenditure.Activity"/>, items
    /// attached). No save — the caller owns the unit of work.
    /// </summary>
    Task AddDraftCopyAsync(
        IReadOnlyList<AipOffice> groups, IReadOnlyList<AipExpenditure> expenditures, CancellationToken ct = default);

    /// <summary>The draft copy's group rows for one SAIP (<c>IgnoreQueryFilters</c>), no tracking.</summary>
    Task<IReadOnlyList<AipOffice>> GetDraftOfficesAsync(int saipId, CancellationToken ct = default);

    /// <summary>
    /// Deletes one SAIP's draft copy: its group rows, and through the database cascade every
    /// program, project, activity, expenditure and item under them. Runs immediately (not staged).
    /// Returns the number of group rows removed. Only rows with this <paramref name="saipId"/> are
    /// touched, so an approved row can never be in it.
    /// </summary>
    Task<int> DeleteDraftCopyAsync(int saipId, CancellationToken ct = default);
}
