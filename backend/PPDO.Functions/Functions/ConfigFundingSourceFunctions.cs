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
/// Funding source config endpoints (<c>/api/config/funding-sources</c>) — RAL-70.
/// All endpoints require a valid JWT with CanManageConfig. Responses use the
/// <c>{ data, error, message }</c> envelope. Soft delete only.
/// </summary>
public sealed class ConfigFundingSourceFunctions
{
    private readonly IFundingSourceService _funding;
    private readonly IJwtMiddleware        _jwt;
    private readonly IPermissionService    _permissions;

    public ConfigFundingSourceFunctions(IFundingSourceService funding, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _funding     = funding;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

    // ── GET /api/config/funding-sources?search=&active=true|false|all ──
    // Any authenticated user may read — funding sources are reference data used in WFP dropdowns.
    [Function("FundingSourcesList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/funding-sources")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        IReadOnlyList<FundingSourceDto> data = await _funding.GetAllAsync(
            req.Query["search"], ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<FundingSourceDto>>.Ok(data), ct);
    }

    // ── GET /api/config/funding-sources/csv ──
    [Function("FundingSourcesCsvExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/funding-sources/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await _funding.ExportCsvAsync(ct);
        return await ConfigHttp.CsvFileAsync(req, csv, "funding-sources.csv", ct);
    }

    // ── GET /api/config/funding-sources/{id} ──
    [Function("FundingSourcesGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/funding-sources/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _funding.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/funding-sources ──
    [Function("FundingSourcesCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/funding-sources")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertFundingSourceDto? body = await ConfigHttp.ReadBodyAsync<UpsertFundingSourceDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<FundingSourceDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _funding.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/funding-sources/csv  (upsert) ──
    [Function("FundingSourcesCsvImport")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/funding-sources/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await ConfigHttp.ReadTextAsync(req);
        ServiceResult<CsvImportResult> result = await _funding.ImportCsvAsync(csv, ct);
        string? message = result.IsSuccess
            ? $"{result.Value!.New} added, {result.Value.Updated} updated, {result.Value.Skipped} skipped."
            : null;
        return await ConfigHttp.FromResultAsync(req, result, ct, message: message);
    }

    // ── PUT /api/config/funding-sources/{id} ──
    [Function("FundingSourcesUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/funding-sources/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertFundingSourceDto? body = await ConfigHttp.ReadBodyAsync<UpsertFundingSourceDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<FundingSourceDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _funding.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/funding-sources/{id}  (soft delete) ──
    [Function("FundingSourcesDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/funding-sources/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _funding.DeleteAsync(id, ct), ct);
    }
}
