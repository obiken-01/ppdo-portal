using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using System.Net;

namespace PPDO.Functions.Functions
{
    /// <summary>
    /// eSRE code config endpoints (<c>/api/config/esre-codes</c>) — RAL-248.
    ///
    /// <b>The list is readable by any authenticated user</b>, because an AIP activity picker
    /// needs it; every write requires CanManageConfig. Writes go through
    /// <c>ConfigHttp.AuthorizeWriteAsync</c> rather than <c>AuthorizeAsync</c>, which is what
    /// applies RAL-256's reviewer write-denial guard — a comment-only cross-office reviewer must
    /// not edit configuration. Responses use the <c>{ data, error, message }</c> envelope.
    /// Delete is a soft delete.
    /// </summary>
    public sealed class ConfigEsreCodeFunctions
    {
        private readonly IEsreCodeService _esreCodes;
        private readonly IJwtMiddleware _jwt;
        private readonly IPermissionService _permissions;

        public ConfigEsreCodeFunctions(
        IEsreCodeService esreCodes, IJwtMiddleware jwt, IPermissionService permissions)
        {
            _esreCodes = esreCodes;
            _jwt = jwt;
            _permissions = permissions;
        }

        private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

        // ── GET /api/config/esre-codes?search=&active=true|false|all ──
        [Function("EsreCodesList")]
        public async Task<HttpResponseData> List(
            [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/esre-codes")] HttpRequestData req,
            CancellationToken ct)
        {
            (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
            if (denied is not null) return denied;

            IReadOnlyList<EsreCodeDto> data = await _esreCodes.GetAllAsync(
                req.Query["search"], ActiveFilterParser.Parse(req.Query["active"]), ct);

            return await ConfigHttp.EnvelopeAsync(
                req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<EsreCodeDto>>.Ok(data), ct);
        }

        // ── GET /api/config/esre-codes/{id} ──
        [Function("EsreCodesGet")]
        public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/esre-codes/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
        {
            (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
            if (denied is not null) return denied;

            return await ConfigHttp.FromResultAsync(req, await _esreCodes.GetByIdAsync(id, ct), ct);
        }

        // ── POST /api/config/esre-codes ──
        [Function("EsreCodesCreate")]
        public async Task<HttpResponseData> Create(
            [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/esre-codes")] HttpRequestData req,
            CancellationToken ct)
        {
            (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageConfig, ct);
            if (denied is not null) return denied;

            UpsertEsreCodeDto? body =
                await ConfigHttp.ReadBodyAsync<UpsertEsreCodeDto>(req, ct);
            if (body is null)
                return await ConfigHttp.EnvelopeAsync(
                    req, HttpStatusCode.BadRequest,
                    ApiResponse<EsreCodeDto>.Fail("Request body is missing or malformed."), ct);

            return await ConfigHttp.FromResultAsync(
                req, await _esreCodes.CreateAsync(body, ct), ct, HttpStatusCode.Created);
        }

        // ── PUT /api/config/esre-codes/{id} ──
        [Function("EsreCodesUpdate")]
        public async Task<HttpResponseData> Update(
            [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/esre-codes/{id:int}")] HttpRequestData req,
            int id, CancellationToken ct)
        {
            (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageConfig, ct);
            if (denied is not null) return denied;

            UpsertEsreCodeDto? body =
                await ConfigHttp.ReadBodyAsync<UpsertEsreCodeDto>(req, ct);
            if (body is null)
                return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                    ApiResponse<EsreCodeDto>.Fail("Request body is missing or malformed."), ct);

            return await ConfigHttp.FromResultAsync(req, await _esreCodes.UpdateAsync(id, body, ct), ct);
        }

        // ── DELETE /api/config/esre-codes/{id} ──
        [Function("EsreCodesDelete")]
        public async Task<HttpResponseData> Delete(
            [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/esre-codes/{id:int}")] HttpRequestData req,
            int id, CancellationToken ct)
        {
            (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManageConfig, ct);
            if (denied is not null) return denied;

            return await ConfigHttp.FromResultAsync(req, await _esreCodes.DeleteAsync(id, ct), ct);
        }
    }
}