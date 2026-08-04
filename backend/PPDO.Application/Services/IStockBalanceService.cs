using PPDO.Application.Common;
using PPDO.Application.DTOs.Inventory;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Contract for the warehouse stock input feature (RAL-193) — a recurring physical-count
/// ledger. Every method requires <c>CanAccessInventory</c> (enforced here, not in the
/// Function handler, per the ResourceLinkService pattern).
/// </summary>
public interface IStockBalanceService
{
    /// <summary>Returns the physical-count history for one StockNo, newest first —
    /// the reconciliation view.</summary>
    Task<ServiceResult<IReadOnlyList<StockBalanceDto>>> GetHistoryAsync(
        User requester, string stockNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the system's current computed on-hand for a StockNo — exactly what a new
    /// entry's SystemOnHandAtEntry would be if saved right now. Reference-only, read-only;
    /// lets the entry form show the user what the system currently expects before they
    /// submit a physical count.
    /// </summary>
    Task<ServiceResult<SystemOnHandDto>> GetSystemOnHandAsync(
        User requester, string stockNo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a new physical count. Snapshots the current system-computed on-hand for the
    /// StockNo and persists CountedQty - SystemOnHandAtEntry as VarianceQty.
    /// </summary>
    Task<ServiceResult<StockBalanceDto>> CreateAsync(
        User requester, CreateStockBalanceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Edits an existing entry's CountedQty/EffectiveDate/Reason and recomputes
    /// VarianceQty against the current system on-hand (excluding this entry's own prior
    /// contribution).</summary>
    Task<ServiceResult<StockBalanceDto>> UpdateAsync(
        User requester, Guid id, UpdateStockBalanceDto dto, CancellationToken cancellationToken = default);

    /// <summary>Deletes an entry. Returns the deleted entry.</summary>
    Task<ServiceResult<StockBalanceDto>> DeleteAsync(
        User requester, Guid id, CancellationToken cancellationToken = default);

    /// <summary>Parses an uploaded workbook for review — nothing is persisted.</summary>
    Task<ServiceResult<StockBalanceImportPreviewDto>> PreviewImportAsync(
        User requester, Stream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the rows the user confirmed after reviewing the preview. Upserts by
    /// StockNo + EffectiveDate — a row matching an existing entry overwrites it (recomputing
    /// VarianceQty); otherwise a new entry is inserted.
    /// </summary>
    Task<ServiceResult<StockBalanceImportResultDto>> CommitImportAsync(
        User requester, CommitStockBalanceImportDto dto, CancellationToken cancellationToken = default);

    /// <summary>Generates the blank bulk-upload Excel template for user download.</summary>
    Task<ServiceResult<byte[]>> GetImportTemplateAsync(
        User requester, CancellationToken cancellationToken = default);
}
