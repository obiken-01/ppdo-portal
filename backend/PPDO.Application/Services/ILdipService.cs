using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// LDIP (Local Development Investment Program) CRUD + status lifecycle (RAL-64, RAL-61).
/// Status workflow: Draft → Final (finalize) → Draft (unlock, admin only) → Archived.
/// v1.3: documents are office-scoped and carry a sector-grouped program hierarchy;
/// AIP ref codes are computed server-side and renumbered on every save.
/// </summary>
public interface ILdipService
{
    Task<IReadOnlyList<LdipRecordDto>> GetAllAsync(
        string? status, int? officeId, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDetailDto>> CreateAsync(CreateLdipDto dto, Guid createdById, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDetailDto>> UpdateAsync(int id, UpdateLdipDto dto, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> FinalizeAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> UnlockAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>Wipes all LDIP records. Kept for post-merge dev testing. Returns deleted count.</summary>
    Task<int> PurgeAllAsync(CancellationToken ct = default);

    // ── File upload (RAL-113) ────────────────────────────────────────────────

    /// <summary>
    /// Parses the uploaded LDIP file — pure parse, no persistence. The workbook
    /// covers every office; every office block found (across all 4 sector sheets)
    /// is matched to a Config → Offices record by AIP ref code and returned grouped
    /// by office.
    /// </summary>
    Task<ServiceResult<LdipImportPreviewDto>> ParsePreviewAsync(
        Stream xlsxStream,
        int fiscalYearStart,
        int fiscalYearEnd,
        IReadOnlyList<FundingSource> knownFundingSources,
        CancellationToken ct = default);

    /// <summary>
    /// Persists the previewed hierarchy as one new Draft LDIP record PER OFFICE
    /// (EntryMode = "Upload") in a single batch.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<LdipRecordDto>>> ConfirmImportAsync(
        LdipImportConfirmDto dto, Guid createdById, CancellationToken ct = default);
}
