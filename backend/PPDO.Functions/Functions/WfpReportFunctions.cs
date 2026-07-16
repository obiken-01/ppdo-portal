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
/// WFP Report preview endpoints under <c>/api/budget-planning/wfp/report</c> (RAL-132).
/// Read-only — gated by CanAccessBudgetPlanning, same as the rest of the WFP surface.
/// GetPreview additionally scopes non-finance callers to their own division (RAL-136).
/// </summary>
public sealed class WfpReportFunctions
{
    private readonly IWfpReportService       _report;
    private readonly IWfpReportExcelService  _excel;
    private readonly IJwtMiddleware          _jwt;
    private readonly IPermissionService      _permissions;

    public WfpReportFunctions(
        IWfpReportService report, IWfpReportExcelService excel,
        IJwtMiddleware jwt, IPermissionService permissions)
    {
        _report      = report;
        _excel       = excel;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);
    private Task<bool> CanManageAllocation(User u) => _permissions.CanManageAllocationAsync(u);

    // ── GET /api/budget-planning/wfp/report/offices?fiscalYear= ──────────────
    // Office picker — only offices with at least a Draft WFP for the fiscal year.
    [Function("WfpReportOffices")]
    public async Task<HttpResponseData> GetEligibleOffices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/offices")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? _, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<WfpReportOfficeDto>>.Fail("fiscalYear query parameter is required."), ct);

        IReadOnlyList<WfpReportOfficeDto> data = await _report.GetEligibleOfficesAsync(fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<WfpReportOfficeDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/wfp/report/preview?officeId=&fiscalYear=&divisionId= ─────────
    // divisionId (RAL-136): division-scoped callers (not CanManageAllocation) are ALWAYS forced
    // to their own division — any divisionId they pass is ignored — so they can never read
    // another division's report by manipulating the query string. Finance officers may pass an
    // optional divisionId to narrow the otherwise-consolidated report to one division.
    [Function("WfpReportPreview")]
    public async Task<HttpResponseData> GetPreview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/preview")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? divisionId;
        if (await CanManageAllocation(caller))
            divisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;
        else
            divisionId = caller.DivisionId;

        ServiceResult<WfpReportDto> result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/wfp/report/export?officeId=&fiscalYear=&divisionId= ─────────
    // Same scoping rules as GetPreview. Returns the PBO-form-shaped .xlsx (RAL-159/v1.4.4).
    [Function("WfpReportExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/export")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? divisionId;
        if (await CanManageAllocation(caller))
            divisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;
        else
            divisionId = caller.DivisionId;

        ServiceResult<WfpReportDto> result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
        if (!result.IsSuccess)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                ApiResponse<WfpReportDto>.Fail(result.Error ?? "Report not found."), ct);

        byte[] bytes = _excel.Export(result.Value!);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            $"attachment; filename=\"WFP_{result.Value!.OfficeCode}_{fiscalYear}.xlsx\"");
        await response.WriteBytesAsync(bytes);
        return response;
    }
}
