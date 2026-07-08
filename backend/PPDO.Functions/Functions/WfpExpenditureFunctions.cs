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
/// WFP expenditure endpoints under <c>/api/budget-planning/wfp/expenditures</c> (RAL-120).
/// Gated by CanAccessBudgetPlanning, same as the rest of the WFP surface (WfpFunctions.cs).
/// Not yet wired to any frontend — this is the schema + computation-pipeline ticket; the
/// entry UI (RAL-123/124/125) calls these endpoints once built.
/// </summary>
public sealed class WfpExpenditureFunctions
{
    private readonly IWfpExpenditureService _expenditures;
    private readonly IJwtMiddleware         _jwt;
    private readonly IPermissionService     _permissions;

    public WfpExpenditureFunctions(
        IWfpExpenditureService expenditures, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _expenditures = expenditures;
        _jwt          = jwt;
        _permissions  = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/wfp/expenditures/{id} ───────────────────────
    [Function("WfpExpenditureGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/expenditures/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? _, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _expenditures.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/wfp/expenditures  (create or replace) ─────
    [Function("WfpExpenditureSave")]
    public async Task<HttpResponseData> Save(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/wfp/expenditures")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? _, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        SaveWfpExpenditureDto? body = await ConfigHttp.ReadBodyAsync<SaveWfpExpenditureDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpExpenditureDto>.Fail("Request body is missing or malformed."), ct);

        ServiceResult<WfpExpenditureDto> result = await _expenditures.SaveExpenditureAsync(body, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct,
            result.IsSuccess ? HttpStatusCode.OK : HttpStatusCode.BadRequest);
    }

    // ── GET /api/budget-planning/wfp/reserve-rate ────────────────────────────
    // Surfaces WfpReserveRule.Rate (RAL-121) so the entry UI (ticket #9) never hard-codes "10%".
    [Function("WfpReserveRateGet")]
    public async Task<HttpResponseData> GetReserveRate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/reserve-rate")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? _, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<WfpReserveRateDto>.Ok(_expenditures.GetReserveRate()), ct);
    }
}
