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
/// WFP endpoints under <c>/api/budget-planning/wfp</c> (RAL-64, RAL-102).
/// All require CanAccessWfp — Budget Planning AND the host office (PPDO-198). Unlock additionally
/// requires Admin/SuperAdmin.
/// SaveAsync is a POST that both creates and updates (upsert by aipRecordId + officeId + divisionId).
///
/// Division scope (RAL-102, PPDO-198): every endpoint resolves the caller's
/// <see cref="WfpDivisionScope"/> and hands it to the service, which checks it against the record.
/// Admin/SuperAdmin/CanManagePpdoAllocation callers see every division and may pass an optional
/// ?divisionId= to list one; others are pinned to their own division, and Staff with no division
/// see nothing.
/// </summary>
public sealed class WfpFunctions
{
    private readonly IWfpService         _wfp;
    private readonly IWfpCeilingService  _ceilings;
    private readonly IJwtValidator      _jwt;
    private readonly IPermissionService  _permissions;

    public WfpFunctions(
        IWfpService wfp, IWfpCeilingService ceilings, IJwtValidator jwt, IPermissionService permissions)
    {
        _wfp         = wfp;
        _ceilings    = ceilings;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessWfpAsync(u);
    private Task<DivisionScope> ScopeOf(User u) => WfpDivisionScope.ResolveAsync(u, _permissions);

    // ── GET /api/budget-planning/wfp?aipRecordId=&officeId=&divisionId= ──────
    [Function("WfpList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/wfp")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        int? aipId    = int.TryParse(req.Query["aipRecordId"], out int a) ? a : null;
        int? officeId = int.TryParse(req.Query["officeId"],    out int o) ? o : null;

        // ?divisionId= narrows an unrestricted caller's list; a scoped caller's is ignored.
        int? divisionId = int.TryParse(req.Query["divisionId"], out int d) ? d : null;

        IReadOnlyList<WfpRecordDto> data = await _wfp.GetAllAsync(aipId, officeId, await ScopeOf(caller!), divisionId, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<WfpRecordDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/wfp/ceilings?activityId=&divisionId=&fiscalYear= ─
    // Ceiling status (§8, RAL-122) — same computation the block-on-save/finalize checks use.
    // Consumed by ticket #9's live client-side validation, not built here.
    [Function("WfpCeilingsGet")]
    public async Task<HttpResponseData> GetCeilings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/ceilings")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["activityId"], out int activityId)   ||
            !int.TryParse(req.Query["divisionId"], out int divisionId)   ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpCeilingStatusDto>.Fail(
                    "activityId, divisionId, and fiscalYear query parameters are required."), ct);

        // PPDO-198: another division's ceiling and spend are that division's figures.
        if (!WfpDivisionScope.Allows(await ScopeOf(caller!), divisionId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                ApiResponse<WfpCeilingStatusDto>.Fail("Division not found."), ct);

        WfpCeilingStatusDto status = await _ceilings.GetStatusAsync(activityId, divisionId, fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<WfpCeilingStatusDto>.Ok(status), ct);
    }

    // ── GET /api/budget-planning/wfp/{id} ────────────────────────────────────
    [Function("WfpGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/wfp/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _wfp.GetByIdAsync(id, await ScopeOf(caller!), ct), ct);
    }

    // ── POST /api/budget-planning/wfp  (upsert) ──────────────────────────────
    [Function("WfpSave")]
    public async Task<HttpResponseData> Save(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/wfp")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        SaveWfpDto? body = await ConfigHttp.ReadBodyAsync<SaveWfpDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpRecordDto>.Fail("Request body is missing or malformed."), ct);

        ServiceResult<WfpRecordDto> result = await _wfp.SaveAsync(body, caller!.Id, await ScopeOf(caller), ct);
        return await ConfigHttp.FromResultAsync(req, result, ct,
            result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
    }

    // ── POST /api/budget-planning/wfp/activities/ensure ──────────────────────
    // Find-or-create the WFP record + activity for (aipRecordId, officeId, divisionId,
    // aipActivityId) — the v1.4 entry wizard's (RAL-123) way to obtain a wfp_activity_id
    // without RAL-102's SaveAsync destructive replace-all semantics.
    [Function("WfpActivityEnsure")]
    public async Task<HttpResponseData> EnsureActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/wfp/activities/ensure")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        EnsureWfpActivityDto? body = await ConfigHttp.ReadBodyAsync<EnsureWfpActivityDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpActivityRefDto>.Fail("Request body is missing or malformed."), ct);

        ServiceResult<WfpActivityRefDto> result = await _wfp.EnsureActivityAsync(
            body.AipRecordId, body.OfficeId, body.DivisionId, body.FiscalYear, body.AipActivityId, caller!.Id,
            await ScopeOf(caller), ct);
        return await ConfigHttp.FromResultAsync(req, result, ct,
            result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
    }

    // ── POST /api/budget-planning/wfp/{id}/finalize ───────────────────────────
    [Function("WfpFinalize")]
    public async Task<HttpResponseData> Finalize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/wfp/{id:int}/finalize")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _wfp.FinalizeAsync(id, await ScopeOf(caller!), ct), ct);
    }

    // ── POST /api/budget-planning/wfp/{id}/unlock  (admin only) ──────────────
    [Function("WfpUnlock")]
    public async Task<HttpResponseData> Unlock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/wfp/{id:int}/unlock")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        if (caller!.Role is not (UserRole.SuperAdmin or UserRole.Admin))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden,
                ApiResponse<WfpRecordDto>.Fail("Admin or SuperAdmin role required to unlock records."), ct);

        return await ConfigHttp.FromResultAsync(req, await _wfp.UnlockAsync(id, await ScopeOf(caller), ct), ct);
    }

    // ── GET /api/budget-planning/wfp/{id}/report ─────────────────────────────
    [Function("WfpExportReport")]
    public async Task<HttpResponseData> ExportReport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/{id:int}/report")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        ServiceResult<byte[]> result = await _wfp.ExportReportAsync(id, caller!, await ScopeOf(caller!), ct);
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
