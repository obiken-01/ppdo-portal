using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// WFP endpoints under <c>/api/budget-planning/wfp</c> (RAL-64).
/// All require CanAccessBudgetPlanning. Unlock additionally requires Admin/SuperAdmin.
/// SaveAsync is a POST that both creates and updates (upsert by aipRecordId + officeId).
/// </summary>
public sealed class WfpFunctions
{
    private readonly IWfpService         _wfp;
    private readonly IJwtMiddleware      _jwt;
    private readonly IPermissionService  _permissions;

    public WfpFunctions(IWfpService wfp, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _wfp         = wfp;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/wfp?aipRecordId=&officeId= ──────────────────
    [Function("WfpList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/wfp")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        int? aipId    = int.TryParse(req.Query["aipRecordId"], out int a) ? a : null;
        int? officeId = int.TryParse(req.Query["officeId"],    out int o) ? o : null;

        IReadOnlyList<WfpRecordDto> data = await _wfp.GetAllAsync(aipId, officeId, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<WfpRecordDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/wfp/{id} ────────────────────────────────────
    [Function("WfpGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/wfp/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _wfp.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/wfp  (upsert) ──────────────────────────────
    [Function("WfpSave")]
    public async Task<HttpResponseData> Save(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/wfp")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        SaveWfpDto? body = await ConfigHttp.ReadBodyAsync<SaveWfpDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpRecordDto>.Fail("Request body is missing or malformed."), ct);

        ServiceResult<WfpRecordDto> result = await _wfp.SaveAsync(body, caller!.Id, ct);
        HttpStatusCode okStatus = result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest;
        return await ConfigHttp.FromResultAsync(req, result, ct,
            result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
    }

    // ── POST /api/budget-planning/wfp/{id}/finalize ───────────────────────────
    [Function("WfpFinalize")]
    public async Task<HttpResponseData> Finalize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/wfp/{id:int}/finalize")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _wfp.FinalizeAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/wfp/{id}/unlock  (admin only) ──────────────
    [Function("WfpUnlock")]
    public async Task<HttpResponseData> Unlock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/wfp/{id:int}/unlock")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        if (caller!.Role is not (UserRole.SuperAdmin or UserRole.Admin))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden,
                ApiResponse<WfpRecordDto>.Fail("Admin or SuperAdmin role required to unlock records."), ct);

        return await ConfigHttp.FromResultAsync(req, await _wfp.UnlockAsync(id, ct), ct);
    }

    // ── GET /api/budget-planning/wfp/{id}/report ─────────────────────────────
    [Function("WfpExportReport")]
    public async Task<HttpResponseData> ExportReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/{id:int}/report")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? _, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        ServiceResult<byte[]> result = await _wfp.ExportReportAsync(id, ct);
        if (!result.IsSuccess)
            return await ConfigHttp.FromResultAsync(req, result, ct);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            $"attachment; filename=\"WFP_Report.xlsx\"");
        await response.WriteBytesAsync(result.Value!);
        return response;
    }
}
