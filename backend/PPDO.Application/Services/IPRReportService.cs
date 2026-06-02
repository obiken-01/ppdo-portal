using PPDO.Application.Common;
using PPDO.Application.DTOs.PurchaseRequest;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Contract for PR Report data retrieval and Excel export.
/// Requires CanAccessInventory or CanAccessReports — enforced in implementations.
/// Division-scope: Staff/Observer can only access their own division's PRs.
/// </summary>
public interface IPRReportService
{
    /// <summary>
    /// Returns the full PR Report as a DTO (Sections 1, 2, and 3).
    /// Section 3 contains the flat distribution list across all delivery events.
    /// </summary>
    Task<ServiceResult<PRReportDto>> GetReportAsync(
        User requester,
        Guid prId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates and returns the PR Report as an .xlsx byte array.
    /// Delegates rendering to <c>IExcelService.ExportPRReport()</c>.
    /// The caller (Function handler) sets Content-Type and Content-Disposition headers.
    /// </summary>
    Task<ServiceResult<byte[]>> ExportReportAsync(
        User requester,
        Guid prId,
        CancellationToken cancellationToken = default);
}
