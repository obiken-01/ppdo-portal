using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services
{
    /// <summary>
    /// CRUD for the eSRE classification vocabulary (RAL-248).
    /// Soft delete only — AIP activities reference these codes on an audited document.
    /// </summary>
    public interface IEsreCodeService
    {
        Task<IReadOnlyList<EsreCodeDto>> GetAllAsync(
            string? search, ActiveFilter active, CancellationToken cancellationToken = default);

        Task<ServiceResult<EsreCodeDto>> GetByIdAsync(
            int id, CancellationToken cancellationToken = default);

        Task<ServiceResult<EsreCodeDto>> CreateAsync(
            UpsertEsreCodeDto dto, CancellationToken cancellationToken = default);

        Task<ServiceResult<EsreCodeDto>> UpdateAsync(
            int id, UpsertEsreCodeDto dto, CancellationToken cancellationToken = default);

        /// <summary>
        /// Counts rows matching the same filters the list applies, in SQL (RAL-260).
        /// Serves the Config dashboard tile, which must never download a list to measure it.
        /// </summary>
        Task<int> GetCountAsync(
            string? search, ActiveFilter active, CancellationToken cancellationToken = default);

        /// <summary>Soft delete — sets IsActive = false. Never removes the row.</summary>
        Task<ServiceResult<EsreCodeDto>> DeleteAsync(
            int id, CancellationToken cancellationToken = default);

        /// <summary>
        /// Exports every code as CSV: <c>code,name,description,is_active</c> (PPDO-19).
        /// Includes inactive rows so an export is a usable backup of the whole table.
        /// </summary>
        Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Upserts codes keyed on <c>code</c>. Returns new/updated/skipped counts (PPDO-19).
        /// Every row this actually changes gets an audit entry; unchanged rows get none.
        ///
        /// ⚠️ <b>This import CAN create a fifth code, deliberately.</b> eSRE is a closed
        /// vocabulary of four today (SS/ES/ID/EN), and PPDO-19 originally asked for unknown codes
        /// to be rejected. Ralph overruled that on 2026-09-02: the vocabulary is closed <i>now</i>,
        /// not forever, and the CSV is how a new code gets in the day the province issues one
        /// without waiting for a release. Validation stays deliberately loose — only <c>code</c>
        /// and <c>name</c> are required, and nothing checks the code against a known list.
        ///
        /// The accepted cost: re-importing an export taken from a database that still holds the
        /// orphaned <c>PPDO/PEO</c> value — an implementing-office name typed into one FY2027
        /// row's eSRE column — recreates it as a real code. See
        /// <see cref="PPDO.Domain.Entities.EsreCode"/> for why that value was left orphaned.
        /// </summary>
        Task<ServiceResult<CsvImportResult>> ImportCsvAsync(
            string csvText, CancellationToken cancellationToken = default);
    }
}