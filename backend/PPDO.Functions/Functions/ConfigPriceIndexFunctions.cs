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
/// Price index config endpoints (<c>/api/config/price-index</c>) — v1.4 RAL-118.
/// All endpoints require a valid JWT. List is readable by any authenticated user
/// (reference data used by the WFP procurement item search, ticket #11/RAL-125);
/// writes require CanManageConfig. Responses use the <c>{ data, error, message }</c>
/// envelope. Soft delete only.
/// </summary>
public sealed class ConfigPriceIndexFunctions
{
    private readonly IPriceIndexService _priceIndex;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public ConfigPriceIndexFunctions(IPriceIndexService priceIndex, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _priceIndex  = priceIndex;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

    // ── GET /api/config/price-index?search=&active=true|false|all ──
    // Any authenticated user may read — price index items are reference data used
    // in the WFP procurement item search (RAL-125).
    [Function("PriceIndexList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/price-index")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        IReadOnlyList<PriceIndexItemDto> data = await _priceIndex.GetAllAsync(
            req.Query["search"], ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<PriceIndexItemDto>>.Ok(data), ct);
    }

    // ── GET /api/config/price-index/csv ──
    [Function("PriceIndexCsvExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/price-index/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await _priceIndex.ExportCsvAsync(ct);
        return await ConfigHttp.CsvFileAsync(req, csv, "price-index.csv", ct);
    }

    // ── GET /api/config/price-index/{id} ──
    [Function("PriceIndexGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/price-index/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _priceIndex.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/price-index ──
    [Function("PriceIndexCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/price-index")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertPriceIndexItemDto? body = await ConfigHttp.ReadBodyAsync<UpsertPriceIndexItemDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<PriceIndexItemDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _priceIndex.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/price-index/csv  (upsert) ──
    [Function("PriceIndexCsvImport")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/price-index/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await ConfigHttp.ReadTextAsync(req);
        ServiceResult<CsvImportResult> result = await _priceIndex.ImportCsvAsync(csv, ct);
        string? message = result.IsSuccess
            ? $"{result.Value!.New} added, {result.Value.Updated} updated, {result.Value.Skipped} skipped."
            : null;
        return await ConfigHttp.FromResultAsync(req, result, ct, message: message);
    }

    // ── PUT /api/config/price-index/{id} ──
    [Function("PriceIndexUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/price-index/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertPriceIndexItemDto? body = await ConfigHttp.ReadBodyAsync<UpsertPriceIndexItemDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<PriceIndexItemDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _priceIndex.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/price-index/{id}  (soft delete) ──
    [Function("PriceIndexDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/price-index/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _priceIndex.DeleteAsync(id, ct), ct);
    }
}
