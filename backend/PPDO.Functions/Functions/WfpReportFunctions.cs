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
/// </summary>
public sealed class WfpReportFunctions
{
    private readonly IWfpReportService  _report;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public WfpReportFunctions(IWfpReportService report, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _report      = report;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

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

    // ── GET /api/budget-planning/wfp/report/preview?officeId=&fiscalYear= ────
    [Function("WfpReportPreview")]
    public async Task<HttpResponseData> GetPreview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/preview")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? _, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        ServiceResult<WfpReportDto> result = await _report.GetReportAsync(officeId, fiscalYear, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }
}
