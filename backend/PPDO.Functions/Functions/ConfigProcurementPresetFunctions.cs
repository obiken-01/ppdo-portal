using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// Procurement preset config-page endpoints (<c>/api/config/procurement-presets</c>) — RAL-119.
/// Full CRUD, gated by CanManageConfig. No CSV import/export — presets are captured from real
/// WFP entries or curated one-by-one here, never bulk-imported (see
/// <see cref="WfpProcurementPresetFunctions"/> for the lighter WFP-entry-screen "quick save"
/// route, gated by CanAccessBudgetPlanning instead, on the same underlying service).
/// Responses use the <c>{ data, error, message }</c> envelope. Soft delete only.
/// </summary>
public sealed class ConfigProcurementPresetFunctions
{
    private readonly IProcurementPresetService _presets;
    private readonly IJwtMiddleware            _jwt;
    private readonly IPermissionService        _permissions;

    public ConfigProcurementPresetFunctions(
        IProcurementPresetService presets, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _presets     = presets;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

    // ── GET /api/config/procurement-presets?accountId=&active=true|false|all ──
    [Function("ProcurementPresetsList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/procurement-presets")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["accountId"], out int accountId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<ProcurementPresetDto>>.Fail("accountId query parameter is required."), ct);

        IReadOnlyList<ProcurementPresetDto> data = await _presets.GetByAccountAsync(
            accountId, ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<ProcurementPresetDto>>.Ok(data), ct);
    }

    // ── GET /api/config/procurement-presets/{id} ──
    [Function("ProcurementPresetsGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/procurement-presets/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _presets.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/procurement-presets ──
    [Function("ProcurementPresetsCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/procurement-presets")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertProcurementPresetDto? body = await ConfigHttp.ReadBodyAsync<UpsertProcurementPresetDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ProcurementPresetDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _presets.CreateAsync(caller!, body, ct), ct, HttpStatusCode.Created);
    }

    // ── PUT /api/config/procurement-presets/{id} ──
    [Function("ProcurementPresetsUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/procurement-presets/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertProcurementPresetDto? body = await ConfigHttp.ReadBodyAsync<UpsertProcurementPresetDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ProcurementPresetDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _presets.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/procurement-presets/{id}  (soft delete) ──
    [Function("ProcurementPresetsDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/procurement-presets/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _presets.DeleteAsync(id, ct), ct);
    }
}
