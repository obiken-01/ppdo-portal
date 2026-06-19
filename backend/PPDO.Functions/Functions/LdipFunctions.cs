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
/// LDIP endpoints under <c>/api/budget-planning/ldip</c> (RAL-64).
/// All require CanAccessBudgetPlanning. The Unlock endpoint additionally requires Admin/SuperAdmin.
/// </summary>
public sealed class LdipFunctions
{
    private readonly ILdipService        _ldip;
    private readonly IJwtMiddleware      _jwt;
    private readonly IPermissionService  _permissions;

    public LdipFunctions(ILdipService ldip, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _ldip        = ldip;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/ldip?status= ────────────────────────────────
    [Function("LdipList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/ldip")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        IReadOnlyList<LdipRecordDto> data = await _ldip.GetAllAsync(req.Query["status"], ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<LdipRecordDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/ldip/{id} ───────────────────────────────────
    [Function("LdipGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/ldip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _ldip.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/ldip ───────────────────────────────────────
    [Function("LdipCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateLdipDto? body = await ConfigHttp.ReadBodyAsync<CreateLdipDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<LdipRecordDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _ldip.CreateAsync(body, caller!.Id, ct), ct, HttpStatusCode.Created);
    }

    // ── PUT /api/budget-planning/ldip/{id} ───────────────────────────────────
    [Function("LdipUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budget-planning/ldip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        UpdateLdipDto? body = await ConfigHttp.ReadBodyAsync<UpdateLdipDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<LdipRecordDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _ldip.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/budget-planning/ldip/{id}  (archive) ─────────────────────
    [Function("LdipArchive")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/ldip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _ldip.ArchiveAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/ldip/{id}/finalize ─────────────────────────
    [Function("LdipFinalize")]
    public async Task<HttpResponseData> Finalize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip/{id:int}/finalize")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _ldip.FinalizeAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/ldip/{id}/unlock  (admin only) ─────────────
    [Function("LdipUnlock")]
    public async Task<HttpResponseData> Unlock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip/{id:int}/unlock")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        if (caller!.Role is not (UserRole.SuperAdmin or UserRole.Admin))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden,
                ApiResponse<LdipRecordDto>.Fail("Admin or SuperAdmin role required to unlock records."), ct);

        return await ConfigHttp.FromResultAsync(req, await _ldip.UnlockAsync(id, ct), ct);
    }
}
