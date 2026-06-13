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
/// Office config endpoints (<c>/api/config/offices</c>) — RAL-70.
/// Writes/exports require CanManageConfig. The list (GET) additionally allows
/// CanManageUsers so the User Management office dropdown keeps working
/// (the dropdown variant is <c>?active=true</c>). Responses use the
/// <c>{ data, error, message }</c> envelope. Soft delete only.
/// </summary>
public sealed class ConfigOfficeFunctions
{
    private readonly IOfficeService     _offices;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public ConfigOfficeFunctions(IOfficeService offices, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _offices     = offices;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

    // List is also used by User Management — allow CanManageUsers OR CanManageConfig.
    private async Task<bool> CanReadOffices(User u)
        => await _permissions.CanManageConfigAsync(u) || await _permissions.CanManageUsersAsync(u);

    // ── GET /api/config/offices?search=&active=true|false|all  (also the dropdown) ──
    [Function("OfficesList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/offices")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanReadOffices, ct);
        if (denied is not null) return denied;

        IReadOnlyList<OfficeDto> data = await _offices.GetAllAsync(
            req.Query["search"], ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<OfficeDto>>.Ok(data), ct);
    }

    // ── GET /api/config/offices/csv ──
    [Function("OfficesCsvExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/offices/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await _offices.ExportCsvAsync(ct);
        return await ConfigHttp.CsvFileAsync(req, csv, "offices.csv", ct);
    }

    // ── GET /api/config/offices/{id} ──
    [Function("OfficesGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/offices/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _offices.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/offices ──
    [Function("OfficesCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/offices")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertOfficeDto? body = await ConfigHttp.ReadBodyAsync<UpsertOfficeDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<OfficeDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _offices.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/offices/csv  (upsert) ──
    [Function("OfficesCsvImport")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/offices/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await ConfigHttp.ReadTextAsync(req);
        ServiceResult<CsvImportResult> result = await _offices.ImportCsvAsync(csv, ct);
        string? message = result.IsSuccess
            ? $"{result.Value!.New} added, {result.Value.Updated} updated, {result.Value.Skipped} skipped."
            : null;
        return await ConfigHttp.FromResultAsync(req, result, ct, message: message);
    }

    // ── PUT /api/config/offices/{id} ──
    [Function("OfficesUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/offices/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertOfficeDto? body = await ConfigHttp.ReadBodyAsync<UpsertOfficeDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<OfficeDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _offices.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/offices/{id}  (soft delete) ──
    [Function("OfficesDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/offices/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _offices.DeleteAsync(id, ct), ct);
    }
}
