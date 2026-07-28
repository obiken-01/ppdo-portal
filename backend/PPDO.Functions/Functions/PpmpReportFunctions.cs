using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// PPMP Report preview endpoint under <c>/api/budget-planning/ppmp/report</c> (v1.5 — RAL-183).
/// Read-only — gated by CanAccessBudgetPlanning, same as the rest of Budget Planning, and scopes
/// non-finance callers to their own division (RAL-136). The office picker is shared with the WFP
/// report (<c>/api/budget-planning/wfp/report/offices</c>) — "offices with a WFP" is identical for
/// both — so this class adds only the preview route.
/// </summary>
public sealed class PpmpReportFunctions
{
    private readonly IPpmpReportService       _report;
    private readonly IPpmpReportExcelService  _excel;
    private readonly IJwtMiddleware           _jwt;
    private readonly IPermissionService       _permissions;

    public PpmpReportFunctions(
        IPpmpReportService report, IPpmpReportExcelService excel,
        IJwtMiddleware jwt, IPermissionService permissions)
    {
        _report      = report;
        _excel       = excel;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);
    private Task<bool> CanManageAllocation(User u) => _permissions.CanManageAllocationAsync(u);

    // ── GET /api/budget-planning/ppmp/report/preview?officeId=&fiscalYear=&divisionId= ─────────
    // divisionId (RAL-136): division-scoped callers (not CanManageAllocation) are ALWAYS forced to
    // their own division — any divisionId they pass is ignored — so they can never read another
    // division's report by manipulating the query string. Allocation officers may pass an optional
    // divisionId to narrow the otherwise-consolidated (office-level) report to one division.
    [Function("PpmpReportPreview")]
    public async Task<HttpResponseData> GetPreview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/ppmp/report/preview")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<PpmpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? divisionId;
        if (await CanManageAllocation(caller))
            divisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;
        else
            divisionId = caller.DivisionId;

        ServiceResult<PpmpReportDto> result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/ppmp/report/export?officeId=&fiscalYear=&divisionId= ─────────
    // Same scoping rules as GetPreview. Returns the province-PPMP-form-shaped .xlsx (RAL-184).
    [Function("PpmpReportExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/ppmp/report/export")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<PpmpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? divisionId;
        if (await CanManageAllocation(caller))
            divisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;
        else
            divisionId = caller.DivisionId;

        ServiceResult<PpmpReportDto> result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
        if (!result.IsSuccess)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                ApiResponse<PpmpReportDto>.Fail(result.Error ?? "Report not found."), ct);

        byte[] bytes = _excel.Export(result.Value!);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            $"attachment; filename=\"PPMP_{result.Value!.OfficeCode}_{fiscalYear}.xlsx\"");
        await response.WriteBytesAsync(bytes);
        return response;
    }
}
