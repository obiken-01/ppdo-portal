using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// AIP (Annual Investment Program) upload + status lifecycle (RAL-64).
/// Entry mode is always "Upload" (XLSM). Manual entry is deferred.
/// Status workflow: Draft → Final (finalize) → Draft (unlock, admin only) → Archived.
/// </summary>
public interface IAipService
{
    Task<IReadOnlyList<AipRecordDto>> GetAllAsync(int? fiscalYear, string? status, CancellationToken ct = default);
    Task<ServiceResult<AipRecordDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Parses an XLSM stream and returns a preview without persisting anything.
    /// <paramref name="knownFundingSources"/> is passed in by the Functions layer so the
    /// service can flag unmatched funding source codes as warnings.
    /// </summary>
    Task<ServiceResult<AipImportPreviewDto>> ParsePreviewAsync(
        Stream xlsmStream,
        int fiscalYear,
        IReadOnlyList<FundingSource> knownFundingSources,
        CancellationToken ct = default);

    /// <summary>
    /// Persists the parsed hierarchy that was returned by <see cref="ParsePreviewAsync"/>.
    /// The client echoes back the full SectorOffices payload (stateless confirm).
    /// </summary>
    Task<ServiceResult<AipRecordDto>> ConfirmImportAsync(
        AipImportConfirmDto dto,
        Guid uploadedById,
        CancellationToken ct = default);

    Task<ServiceResult<AipRecordDto>> FinalizeAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<AipRecordDto>> UnlockAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<AipRecordDto>> ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>Wipes all AIP records (cascade removes hierarchy). Returns deleted AipRecord count.</summary>
    Task<int> PurgeAllAsync(CancellationToken ct = default);
}
