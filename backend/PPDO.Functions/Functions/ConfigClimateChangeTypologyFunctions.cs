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
/// CCET climate-change typology config endpoints (<c>/api/config/cc-typologies</c>) — RAL-247.
/// Writes require CanManageConfig; the list is readable by any authenticated user because it is
/// reference data an AIP activity picker needs. Responses use the <c>{ data, error, message }</c>
/// envelope. Soft delete only.
/// </summary>
public sealed class ConfigClimateChangeTypologyFunctions
{
    private readonly IClimateChangeTypologyService _typologies;
    private readonly IJwtMiddleware                _jwt;
    private readonly IPermissionService            _permissions;

    public ConfigClimateChangeTypologyFunctions(
        IClimateChangeTypologyService typologies, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _typologies  = typologies;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

    // ── GET /api/config/cc-typologies?search=&active=true|false|all ──
    [Function("CcTypologiesList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/cc-typologies")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        IReadOnlyList<ClimateChangeTypologyDto> data = await _typologies.GetAllAsync(
            req.Query["search"], ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(
            req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<ClimateChangeTypologyDto>>.Ok(data), ct);
    }

    // ── GET /api/config/cc-typologies/count?search=&active=true|false|all ──
    // Serves the Config dashboard tile. Same auth and same filters as List, so the two can
    // never disagree — the shape RAL-232 established after a tile downloaded 1.57 MB to
    // render a number.
    [Function("CcTypologiesCount")]
    public async Task<HttpResponseData> Count(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/cc-typologies/count")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        int count = await _typologies.GetCountAsync(
            req.Query["search"], ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<int>.Ok(count), ct);
    }

    // ── GET /api/config/cc-typologies/{id} ──
    [Function("CcTypologiesGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/cc-typologies/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _typologies.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/cc-typologies ──
    [Function("CcTypologiesCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/cc-typologies")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertClimateChangeTypologyDto? body =
            await ConfigHttp.ReadBodyAsync<UpsertClimateChangeTypologyDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ClimateChangeTypologyDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(
            req, await _typologies.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── PUT /api/config/cc-typologies/{id} ──
    [Function("CcTypologiesUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/cc-typologies/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertClimateChangeTypologyDto? body =
            await ConfigHttp.ReadBodyAsync<UpsertClimateChangeTypologyDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ClimateChangeTypologyDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _typologies.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/cc-typologies/{id}  (soft delete) ──
    [Function("CcTypologiesDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/cc-typologies/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _typologies.DeleteAsync(id, ct), ct);
    }
}
