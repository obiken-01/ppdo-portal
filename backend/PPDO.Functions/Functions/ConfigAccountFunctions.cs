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
/// Chart of Accounts config endpoints (<c>/api/config/accounts</c>) — RAL-70.
/// All endpoints require a valid JWT with CanManageConfig. Responses use the
/// <c>{ data, error, message }</c> envelope. Soft delete only.
/// </summary>
public sealed class ConfigAccountFunctions
{
    private readonly IAccountService    _accounts;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public ConfigAccountFunctions(IAccountService accounts, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _accounts    = accounts;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

    // ── GET /api/config/accounts?search=&accountType=PS|MOOE|CO&active=true|false|all ──
    [Function("AccountsList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/accounts")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string? accountType = req.Query["accountType"];
        if (!string.IsNullOrWhiteSpace(accountType)
            && accountType.Trim().ToUpperInvariant() is not ("PS" or "MOOE" or "CO"))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<object>.Fail("accountType must be PS, MOOE, or CO."), ct);

        IReadOnlyList<AccountDto> data = await _accounts.GetAllAsync(
            req.Query["search"], accountType, ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<AccountDto>>.Ok(data), ct);
    }

    // ── GET /api/config/accounts/csv ──
    [Function("AccountsCsvExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/accounts/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await _accounts.ExportCsvAsync(ct);
        return await ConfigHttp.CsvFileAsync(req, csv, "accounts.csv", ct);
    }

    // ── GET /api/config/accounts/{id} ──
    [Function("AccountsGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/accounts/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _accounts.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/accounts ──
    [Function("AccountsCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/accounts")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertAccountDto? body = await ConfigHttp.ReadBodyAsync<UpsertAccountDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AccountDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _accounts.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/accounts/csv  (upsert) ──
    [Function("AccountsCsvImport")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/accounts/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await ConfigHttp.ReadTextAsync(req);
        ServiceResult<CsvImportResult> result = await _accounts.ImportCsvAsync(csv, ct);
        string? message = result.IsSuccess
            ? $"{result.Value!.New} added, {result.Value.Updated} updated, {result.Value.Skipped} skipped."
            : null;
        return await ConfigHttp.FromResultAsync(req, result, ct, message: message);
    }

    // ── PUT /api/config/accounts/{id} ──
    [Function("AccountsUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/accounts/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        UpsertAccountDto? body = await ConfigHttp.ReadBodyAsync<UpsertAccountDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AccountDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _accounts.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/accounts/{id}  (soft delete) ──
    [Function("AccountsDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/accounts/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _accounts.DeleteAsync(id, ct), ct);
    }
}
