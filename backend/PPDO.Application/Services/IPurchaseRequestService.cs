using PPDO.Application.Common;
using PPDO.Application.DTOs.PurchaseRequest;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Application.Services;

/// <summary>
/// Contract for all Purchase Request business logic.
/// Division-scope enforcement, PR number generation, and Items Master auto-creation
/// all live here — never in the Function handler.
/// </summary>
public interface IPurchaseRequestService
{
    /// <summary>
    /// Returns all PRs visible to the requester, optionally filtered by status.
    /// Staff/Observer: own division only. Admin/SuperAdmin: all divisions.
    /// Pass status to power the PR Register filtered view (e.g. Open only).
    /// </summary>
    Task<IReadOnlyList<PRSummaryDto>> GetAllAsync(
        User requester,
        PRStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the full PR detail (header + line items).
    /// Returns Forbidden if a Staff/Observer tries to view another division's PR.
    /// </summary>
    Task<ServiceResult<PRResponseDto>> GetByIdAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Submits a new PR.
    /// Generates the PR number (101-1041-GF-YYYY-MM-DD-XXX in Manila time).
    /// Auto-creates unknown StockNos in Items Master with IsNewItem = true.
    /// Staff can only submit for their own division.
    /// </summary>
    Task<ServiceResult<PRResponseDto>> CreateAsync(
        User requester,
        CreatePRDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing PR. Admin/SuperAdmin only. PR must be in Open status.
    /// Re-computes TotalAmount when items are replaced.
    /// Auto-creates unknown StockNos with IsNewItem = true (same as CreateAsync).
    /// </summary>
    Task<ServiceResult<PRResponseDto>> UpdateAsync(
        User requester,
        Guid id,
        UpdatePRDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the blank PR Excel template as a byte array.
    /// Delegates to IExcelService.GeneratePRTemplate().
    /// Requires CanAccessInventory.
    /// </summary>
    Task<ServiceResult<byte[]>> GetTemplateAsync(
        User requester,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Parses an uploaded PR Excel file and creates one PR per worksheet.
    /// Delegates parsing to IExcelService.ParsePRImport(), then calls CreateAsync for each row.
    /// Requires CanAccessInventory.
    /// Returns Forbidden if any worksheet targets a division the requester cannot write to.
    /// The whole import is rejected if any worksheet fails validation (ExcelParseException).
    /// </summary>
    Task<ServiceResult<IReadOnlyList<PRResponseDto>>> ImportFromExcelAsync(
        User requester,
        Stream stream,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a PR as Completed. Only valid when current status is FullyDelivered.
    /// Requires CanAccessInventory. Division scope is not enforced — any
    /// inventory-permitted user can close a fully delivered PR.
    /// </summary>
    Task<ServiceResult<PRSummaryDto>> MarkCompletedAsync(
        User requester,
        Guid id,
        CancellationToken cancellationToken = default);
}
