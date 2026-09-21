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
/// AIP expenditure lines — the third stage of entry (V18-42 / PPDO-52, spec §4).
///
/// <para>
/// Its own Functions class rather than four more handlers on <c>AipFunctions</c>, which already
/// carries 24 routes. One file per feature group, per CLAUDE.md.
/// </para>
///
/// <para>
/// All four routes are gated on <c>CanAccessBudgetPlanning</c> and none is public. Ownership,
/// record state and the office's workflow state are enforced in the service via
/// <see cref="AipWriteGuard"/> — the handler validates the body and returns the result, and holds
/// no business logic of its own.
/// </para>
/// </summary>
public sealed class AipExpenditureFunctions
{
    private readonly IAipExpenditureService _expenditures;
    private readonly IJwtMiddleware         _jwt;
    private readonly IPermissionService     _permissions;

    public AipExpenditureFunctions(
        IAipExpenditureService expenditures,
        IJwtMiddleware         jwt,
        IPermissionService     permissions)
    {
        _expenditures = expenditures;
        _jwt          = jwt;
        _permissions  = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/aip/activities/{activityId}/expenditures ─────
    [Function("AipListExpenditures")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/activities/{activityId:int}/expenditures")] HttpRequestData req,
        int activityId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _expenditures.GetByActivityAsync(activityId, caller!, ct), ct);
    }

    // ── POST /api/budget-planning/aip/activities/{activityId}/expenditures ────
    [Function("AipAddExpenditure")]
    public async Task<HttpResponseData> Add(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/activities/{activityId:int}/expenditures")] HttpRequestData req,
        int activityId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        CreateAipExpenditureDto? body = await ConfigHttp.ReadBodyAsync<CreateAipExpenditureDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipExpenditureWriteResultDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _expenditures.AddAsync(activityId, body, caller!, ct), ct, HttpStatusCode.Created);
    }

    // ── PUT /api/budget-planning/aip/expenditures/{id} ───────────────────────
    [Function("AipUpdateExpenditure")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/aip/expenditures/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        UpdateAipExpenditureDto? body = await ConfigHttp.ReadBodyAsync<UpdateAipExpenditureDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipExpenditureWriteResultDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _expenditures.UpdateAsync(id, body, caller!, ct), ct);
    }

    // ── DELETE /api/budget-planning/aip/expenditures/{id} ────────────────────
    [Function("AipDeleteExpenditure")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete",
            Route = "budget-planning/aip/expenditures/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        // ⚠️ Returns the recomputed activity rather than 204, deliberately. Deleting the last line
        // takes the activity's total to 0 and the caller must render that; a bare 204 would leave
        // the page showing the pre-delete figure until someone reloaded.
        return await ConfigHttp.FromResultAsync(req,
            await _expenditures.DeleteAsync(id, caller!, ct), ct);
    }
}
