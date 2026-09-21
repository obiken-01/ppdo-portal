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
/// Configuration → API Access (<c>/api/config/api-keys</c>) — v1.8.0 PPDO-86, build spec §4.2/§6.1.
///
/// Every route here — list included — requires <c>CanManageApiKeys</c> (build spec §3.3: a caller
/// without the grant gets 403 even on a read). This is unlike <see cref="ConfigEsreCodeFunctions"/>,
/// whose list is broadly readable; nothing else needs to read partner keys.
/// </summary>
public sealed class ConfigApiKeysFunctions
{
    private readonly IPartnerApiKeyService _keys;
    private readonly IJwtMiddleware _jwt;
    private readonly IPermissionService _permissions;

    public ConfigApiKeysFunctions(
        IPartnerApiKeyService keys, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _keys = keys;
        _jwt = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageApiKeys(User u) => _permissions.CanManageApiKeysAsync(u);

    // ── GET /api/config/api-keys ──
    [Function("ApiKeysList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/api-keys")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageApiKeys, ct);
        if (denied is not null) return denied;

        IReadOnlyList<ApiKeyListItemDto> data = await _keys.GetAllAsync(ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<ApiKeyListItemDto>>.Ok(data), ct);
    }

    // ── POST /api/config/api-keys ──
    [Function("ApiKeysCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/api-keys")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageApiKeys, ct);
        if (denied is not null) return denied;

        CreateApiKeyDto? body = await ConfigHttp.ReadBodyAsync<CreateApiKeyDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(
                req, HttpStatusCode.BadRequest,
                ApiResponse<CreateApiKeyResultDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(
            req, await _keys.IssueAsync(caller!.Id, body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/api-keys/{id}/revoke ──
    [Function("ApiKeysRevoke")]
    public async Task<HttpResponseData> Revoke(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/api-keys/{id:int}/revoke")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageApiKeys, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _keys.RevokeAsync(caller!.Id, id, ct), ct);
    }

    // ── GET /api/config/api-keys/{id}/requests?page=1 ──
    [Function("ApiKeysRequests")]
    public async Task<HttpResponseData> Requests(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/api-keys/{id:int}/requests")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageApiKeys, ct);
        if (denied is not null) return denied;

        int page = int.TryParse(req.Query["page"], out int p) ? p : 1;
        return await ConfigHttp.FromResultAsync(req, await _keys.GetRequestsAsync(id, page, ct), ct);
    }
}
