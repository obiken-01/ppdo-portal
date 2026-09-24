using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Funding source config CRUD + CSV upsert/export (RAL-70).
/// Soft delete only (IsActive = false). Code is the unique key.
///
/// <b>v1.8.0 (PPDO-109) — two kinds of row.</b> A fund with no office is province-wide and PPDO's;
/// a fund with one belongs to that office alone (D5). Every read here takes
/// <c>visibleToOfficeId</c>, and the ONE rule that governs it is stated once, on
/// <see cref="GetAllAsync"/>. Who may write which row is decided in
/// <c>ConfigFundingSourceFunctions</c>, next to the permission check that answers it.
/// </summary>
public interface IFundingSourceService
{
    /// <summary>
    /// <paramref name="search"/> matches code OR name (case-insensitive, contains).
    ///
    /// <para><paramref name="visibleToOfficeId"/> is the office whose funds the caller may see:
    /// <list type="bullet">
    ///   <item><description><c>null</c> — no office filter: every row, shared and office-owned.
    ///   This is the config manager's view and the only one that crosses offices.</description></item>
    ///   <item><description>an office id — shared rows <b>plus</b> that office's own. Note it is
    ///   plus, not instead of: an encoder needs the General Fund as much as their own
    ///   office's additions (D5).</description></item>
    /// </list>
    /// A caller with no office resolves to <c>OfficeScope.NoOffice</c> (0), which nothing owns, so
    /// they see the shared rows and nothing else — the safe degradation, not full access.</para>
    /// </summary>
    Task<IReadOnlyList<FundingSourceDto>> GetAllAsync(
        string? search, ActiveFilter active, int? visibleToOfficeId = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<FundingSourceDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<FundingSourceDto>> CreateAsync(UpsertFundingSourceDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<FundingSourceDto>> UpdateAsync(int id, UpsertFundingSourceDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// What limiting fund <paramref name="id"/> to office <paramref name="targetOfficeId"/> would
    /// take away from every other office (PPDO-128) — the preview the config page shows before it
    /// asks PPDO to confirm. A null target (make it shared) or the fund's current owner hides it
    /// from nobody new, so both answer zero without counting.
    /// </summary>
    Task<ServiceResult<FundOwnershipImpactDto>> GetOwnershipImpactAsync(
        int id, int? targetOfficeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deactivates the fund (soft delete — history is never broken).
    ///
    /// <para><paramref name="blockWhenInUse"/> adds the PPDO-109 usage guard: the call is refused
    /// with a <see cref="ServiceErrorCode.Conflict"/> naming the row count when any WFP or AIP line
    /// still references the fund.</para>
    ///
    /// ⚠️ <b>Off by default, and that is deliberate.</b> Soft delete exists precisely so a fund with
    /// years of history can be retired from the pickers while its records keep resolving, so a
    /// config manager keeps the unconditional behaviour they have always had. The guard is for a
    /// department head retiring a fund they added themselves: theirs is new, small and better
    /// protected than orphaned. Turning it on for everyone would take away PPDO's ability to retire
    /// any fund that was ever used, which is all of them.
    /// </summary>
    Task<ServiceResult<FundingSourceDto>> DeleteAsync(
        int id, bool blockWhenInUse = false, CancellationToken cancellationToken = default);

    /// <summary>Exports all funding sources as CSV: code, name, description, is_active.</summary>
    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts funding sources by code. Returns new/updated/skipped counts.</summary>
    Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default);
}
