using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.PurchaseRequest;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// PR Report — loads the full entity graph for Sections 1, 2, and 3,
/// maps to response DTOs, and delegates Excel rendering to IExcelService.
///
/// Loading strategy (respects the 2-level Include depth rule):
///   1. PR with Items         → _prs.GetWithItemsAsync()
///   2. Deliveries with Items + Distributions + PRItem
///                           → _deliveries.GetDeliveriesForPRReportAsync()
///   3. Wire Deliveries onto PR.Deliveries in memory before passing to ExcelService.
/// </summary>
public sealed class PRReportService : IPRReportService
{
    private readonly IPurchaseRequestRepository _prs;
    private readonly IDeliveryRepository        _deliveries;
    private readonly IPermissionService         _permissions;
    private readonly IExcelService              _excel;
    private readonly ILogger<PRReportService>   _logger;

    public PRReportService(
        IPurchaseRequestRepository prs,
        IDeliveryRepository deliveries,
        IPermissionService permissions,
        IExcelService excel,
        ILogger<PRReportService> logger)
    {
        _prs         = prs;
        _deliveries  = deliveries;
        _permissions = permissions;
        _excel       = excel;
        _logger      = logger;
    }

    // ── GetReportAsync ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<PRReportDto>> GetReportAsync(
        User requester,
        Guid prId,
        CancellationToken cancellationToken = default)
    {
        ServiceResult<(PurchaseRequest pr, IReadOnlyList<Delivery> deliveries)> loaded =
            await LoadAndAuthorizeAsync(requester, prId, cancellationToken);

        if (!loaded.IsSuccess)
            return ServiceResult<PRReportDto>.FromError(loaded);

        (PurchaseRequest pr, IReadOnlyList<Delivery> deliveries) = loaded.Value!;

        PRResponseDto prDto = MapToPRResponse(pr);

        List<PRReportDistributionDto> distributions = BuildDistributionRows(deliveries);

        return ServiceResult<PRReportDto>.Ok(new PRReportDto(prDto, distributions));
    }

    // ── ExportReportAsync ──────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<byte[]>> ExportReportAsync(
        User requester,
        Guid prId,
        CancellationToken cancellationToken = default)
    {
        ServiceResult<(PurchaseRequest pr, IReadOnlyList<Delivery> deliveries)> loaded =
            await LoadAndAuthorizeAsync(requester, prId, cancellationToken);

        if (!loaded.IsSuccess)
            return ServiceResult<byte[]>.FromError(loaded);

        (PurchaseRequest pr, IReadOnlyList<Delivery> deliveries) = loaded.Value!;

        // Wire the loaded deliveries onto the PR entity so ExcelService can traverse
        // pr.Deliveries → delivery.Items → di.Distributions and di.PRItem.
        pr.Deliveries = deliveries.ToList();

        byte[] bytes;
        try
        {
            bytes = _excel.ExportPRReport(pr);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to generate Excel export for PR {PRNo}.", pr.PRNo);
            return ServiceResult<byte[]>.BadRequest(
                "Excel export failed — the report could not be generated.");
        }

        return ServiceResult<byte[]>.Ok(bytes);
    }

    // ── Shared load + authorize ────────────────────────────────────────────────

    /// <summary>
    /// Loads PR with items and all delivery data, enforces division scope and permission.
    /// Returns a typed tuple so both public methods share the same auth/load logic.
    /// </summary>
    private async Task<ServiceResult<(PurchaseRequest, IReadOnlyList<Delivery>)>>
        LoadAndAuthorizeAsync(
            User requester,
            Guid prId,
            CancellationToken cancellationToken)
    {
        if (!await _permissions.CanAccessInventoryAsync(requester, cancellationToken))
            return ServiceResult<(PurchaseRequest, IReadOnlyList<Delivery>)>.Forbidden(
                "You do not have permission to access Inventory.");

        PurchaseRequest? pr = await _prs.GetWithItemsAsync(prId, cancellationToken);
        if (pr is null)
            return ServiceResult<(PurchaseRequest, IReadOnlyList<Delivery>)>.NotFound(
                $"Purchase Request {prId} not found.");

        if (requester.Role is UserRole.Staff or UserRole.Observer
            && pr.Division != requester.Division)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to access report for PR {PRNo}.",
                requester.Id, pr.PRNo);
            return ServiceResult<(PurchaseRequest, IReadOnlyList<Delivery>)>.Forbidden(
                "You can only access reports for your own division's Purchase Requests.");
        }

        IReadOnlyList<Delivery> deliveries =
            await _deliveries.GetDeliveriesForPRReportAsync(prId, cancellationToken);

        return ServiceResult<(PurchaseRequest, IReadOnlyList<Delivery>)>.Ok((pr, deliveries));
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    private static PRResponseDto MapToPRResponse(PurchaseRequest pr) => new(
        pr.Id, pr.PRNo, pr.PRDate, pr.DateCreated,
        pr.Department, pr.Division, pr.Fund,
        pr.RequestedBy, pr.Position,
        pr.ApprovedBy, pr.ApprovingPosition,
        pr.AIPCode, pr.AccountNo, pr.AccountTitle,
        pr.Program, pr.Project, pr.Activity,
        pr.SAINo, pr.ALOBSNo,
        pr.TotalAmount, pr.Status.ToString(),
        pr.CreatedById, pr.CreatedAt, pr.UpdatedAt,
        pr.Items.OrderBy(i => i.ItemNo)
                .Select(i => new PRItemDto(
                    i.Id, i.PRId, i.ItemNo, i.StockNo,
                    i.Description, i.Unit, i.Quantity,
                    i.UnitCost, i.TotalCost, i.ItemType))
                .ToList());

    private static List<PRReportDistributionDto> BuildDistributionRows(
        IReadOnlyList<Delivery> deliveries)
    {
        List<PRReportDistributionDto> rows = new();

        foreach (Delivery delivery in deliveries)
        {
            foreach (DeliveryItem di in delivery.Items)
            {
                foreach (Distribution dist in di.Distributions)
                {
                    rows.Add(new PRReportDistributionDto(
                        ItemNo:       di.PRItem?.ItemNo ?? 0,
                        Description:  di.PRItem?.Description ?? string.Empty,
                        Unit:         di.PRItem?.Unit ?? string.Empty,
                        QtyDelivered: di.QtyDelivered,
                        DeliveryRef:  delivery.DeliveryRef,
                        DeliveryDate: delivery.DeliveryDate,
                        Division:     dist.Division,
                        QtyIssued:    dist.QtyIssued,
                        IssueRef:     dist.IssueRef,
                        DateIssued:   dist.DateIssued,
                        IssuedBy:     dist.IssuedBy,
                        Remarks:      dist.Remarks));
                }
            }
        }

        return rows
            .OrderBy(r => r.ItemNo)
            .ThenBy(r => r.DeliveryDate)
            .ToList();
    }
}
