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
/// Division config endpoints (<c>/api/config/divisions</c>) — RAL-97 (GET list) + RAL-98 (full CRUD + CSV).
///
/// The GET list allows CanManageConfig OR CanManageUsers so the User Management division dropdown
/// keeps working. All writes (POST/PUT/DELETE/CSV) require CanManageConfig. Responses use the
/// <c>{ data, error, message }</c> envelope. Soft delete only.
/// </summary>
public sealed class ConfigDivisionFunctions
{
    private readonly IDivisionService   _divisions;
    private readonly IOfficeService     _offices;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public ConfigDivisionFunctions(
        IDivisionService   divisions,
        IOfficeService     offices,
        IJwtMiddleware     jwt,
        IPermissionService permissions)
    {
        _divisions   = divisions;
        _offices     = offices;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u)  => _permissions.CanManageConfigAsync(u);

    private async Task<bool> CanReadDivisions(User u)
        => await _permissions.CanManageConfigAsync(u) || await _permissions.CanManageUsersAsync(u);

    // ── GET /api/config/divisions?active=true&officeId=  (also the user-form dropdown) ──
    [Function("DivisionsList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanReadDivisions, ct);
        if (denied is not null) return denied;

        bool? activeOnly = (req.Query["active"] ?? "").Trim().ToLowerInvariant() switch
        {
            "true"  => true,
            "false" => false,
            _       => null,
        };
        int? officeId = int.TryParse(req.Query["officeId"], out int oid) ? oid : null;

        IReadOnlyList<DivisionDto> data = await _divisions.GetAllAsync(activeOnly, officeId, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<DivisionDto>>.Ok(data), ct);
    }

    // ── GET /api/config/divisions/csv ──
    [Function("DivisionsCsvExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/divisions/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        IReadOnlyList<OfficeDto> officeDtos = await _offices.GetAllAsync(search: null, ActiveFilter.All, ct);
        IReadOnlyList<Office> offices = officeDtos.Select(o => new Office
        {
            Id = o.Id, OfficeCode = o.OfficeCode, OfficeName = o.OfficeName,
            IsActive = o.IsActive, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }).ToList();

        string csv = await _divisions.ExportCsvAsync(offices, ct);
        return await ConfigHttp.CsvFileAsync(req, csv, "divisions.csv", ct);
    }

    // ── GET /api/config/divisions/{id} ──
    [Function("DivisionsGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/divisions/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _divisions.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/divisions ──
    [Function("DivisionsCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertDivisionDto? body = await ConfigHttp.ReadBodyAsync<UpsertDivisionDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<DivisionDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _divisions.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/divisions/csv  (upsert) ──
    [Function("DivisionsCsvImport")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/divisions/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        IReadOnlyList<OfficeDto> officeDtos = await _offices.GetAllAsync(search: null, ActiveFilter.All, ct);
        IReadOnlyList<Office> offices = officeDtos.Select(o => new Office
        {
            Id = o.Id, OfficeCode = o.OfficeCode, OfficeName = o.OfficeName,
            IsActive = o.IsActive, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }).ToList();

        string csv = await ConfigHttp.ReadTextAsync(req);
        ServiceResult<CsvImportResult> result = await _divisions.ImportCsvAsync(csv, offices, ct);
        string? message = result.IsSuccess
            ? $"{result.Value!.New} added, {result.Value.Updated} updated, {result.Value.Skipped} skipped."
            : null;
        return await ConfigHttp.FromResultAsync(req, result, ct, message: message);
    }

    // ── PUT /api/config/divisions/{id} ──
    [Function("DivisionsUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/divisions/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertDivisionDto? body = await ConfigHttp.ReadBodyAsync<UpsertDivisionDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<DivisionDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _divisions.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/divisions/{id}  (soft delete) ──
    [Function("DivisionsDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/divisions/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _divisions.DeleteAsync(id, ct), ct);
    }
}
