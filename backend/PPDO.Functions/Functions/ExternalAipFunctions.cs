using System.Collections.Specialized;
using System.Net;
using System.Text.Json;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// External AIP API for partner systems — <c>/api/external/v1</c> (v1.8.0 — PPDO-12,
/// <c>docs/v1.8/External_AIP_API_Spec.md</c> §4.1). Authenticated by <c>X-Api-Key</c>
/// (<see cref="ExternalApiHttp"/>, PPDO-13); the payload is shaped by
/// <see cref="IExternalAipReadService"/> (PPDO-14) — see
/// <c>docs/external-api/aip-response.schema.json</c> for the contract.
///
/// The health route is public and returns a bare <c>{ status, timestamp }</c>; the two data
/// routes need a key and use the <c>{ data, error, message }</c> envelope, including on error
/// (build spec §2 decision 11) — never a bare status code.
/// </summary>
public sealed class ExternalAipFunctions
{
    private readonly IPartnerCredentialValidator _credentials;
    private readonly IPartnerApiRateLimiter _rateLimiter;
    private readonly IPartnerApiRequestLogger _requestLogger;
    private readonly IExternalAipReadService _reads;
    private readonly IOfficeRepository _offices;

    public ExternalAipFunctions(
        IPartnerCredentialValidator credentials,
        IPartnerApiRateLimiter rateLimiter,
        IPartnerApiRequestLogger requestLogger,
        IExternalAipReadService reads,
        IOfficeRepository offices)
    {
        _credentials = credentials;
        _rateLimiter = rateLimiter;
        _requestLogger = requestLogger;
        _reads = reads;
        _offices = offices;
    }

    // ── GET /api/external/v1/health — public, no key ──────────────────────────
    [Function("ExternalAipHealth")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "external/v1/health")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        // No database call — this wakes the Functions host only. The internal /api/health
        // already covers SQL connectivity.
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(
                new { status = "ok", timestamp = DateTime.UtcNow.ToString("o") }, ConfigHttp.Json),
            cancellationToken);
        return response;
    }

    // ── GET /api/external/v1/aip?fiscalYear=&officeCode= ──────────────────────
    [Function("ExternalAip")]
    public async Task<HttpResponseData> GetAip(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "external/v1/aip")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        (PartnerApiKey? key, HttpResponseData? denied) =
            await ExternalApiHttp.AuthenticateAsync(req, _credentials, _rateLimiter, cancellationToken);
        if (denied is not null) return denied;

        NameValueCollection query = HttpUtility.ParseQueryString(req.Url.Query);
        string? officeCode = string.IsNullOrWhiteSpace(query["officeCode"]) ? null : query["officeCode"];

        if (!int.TryParse(query["fiscalYear"], out int fiscalYear))
        {
            HttpResponseData bad = await ConfigHttp.EnvelopeAsync(
                req, HttpStatusCode.BadRequest,
                ApiResponse<ExternalAipDto?>.Fail("fiscalYear is required."), cancellationToken);
            await _requestLogger.LogAsync(key!, "aip", officeCode, null, (int)bad.StatusCode, cancellationToken);
            return bad;
        }

        Office? resolvedOffice = officeCode is null
            ? null
            : await _offices.GetByCodeAsync(officeCode, cancellationToken);

        ServiceResult<bool> scope = PartnerApiScope.Authorize(key!, officeCode, resolvedOffice);
        if (!scope.IsSuccess)
        {
            HttpResponseData refused = await ConfigHttp.FromResultAsync(
                req, ServiceResult<ExternalAipDto?>.FromError(scope), cancellationToken);
            await _requestLogger.LogAsync(key!, "aip", officeCode, fiscalYear, (int)refused.StatusCode, cancellationToken);
            return refused;
        }

        ExternalAipDto? data = await _reads.GetAsync(fiscalYear, resolvedOffice, cancellationToken);
        HttpResponseData ok = await ConfigHttp.EnvelopeAsync(
            req, HttpStatusCode.OK, ApiResponse<ExternalAipDto?>.Ok(data), cancellationToken);
        await _requestLogger.LogAsync(key!, "aip", officeCode, fiscalYear, (int)ok.StatusCode, cancellationToken);
        return ok;
    }

    // ── GET /api/external/v1/aip/fiscal-years?officeCode= ─────────────────────
    [Function("ExternalAipFiscalYears")]
    public async Task<HttpResponseData> GetFiscalYears(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "external/v1/aip/fiscal-years")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        (PartnerApiKey? key, HttpResponseData? denied) =
            await ExternalApiHttp.AuthenticateAsync(req, _credentials, _rateLimiter, cancellationToken);
        if (denied is not null) return denied;

        NameValueCollection query = HttpUtility.ParseQueryString(req.Url.Query);
        string? officeCode = string.IsNullOrWhiteSpace(query["officeCode"]) ? null : query["officeCode"];

        Office? resolvedOffice = officeCode is null
            ? null
            : await _offices.GetByCodeAsync(officeCode, cancellationToken);

        // Same scope rule as /aip (build spec §4.1: "Same scope rules as /aip").
        ServiceResult<bool> scope = PartnerApiScope.Authorize(key!, officeCode, resolvedOffice);
        if (!scope.IsSuccess)
        {
            HttpResponseData refused = await ConfigHttp.FromResultAsync(
                req, ServiceResult<IReadOnlyList<int>>.FromError(scope), cancellationToken);
            await _requestLogger.LogAsync(
                key!, "aip/fiscal-years", officeCode, null, (int)refused.StatusCode, cancellationToken);
            return refused;
        }

        IReadOnlyList<int> years = await _reads.GetFiscalYearsAsync(resolvedOffice, cancellationToken);
        HttpResponseData ok = await ConfigHttp.EnvelopeAsync(
            req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<int>>.Ok(years), cancellationToken);
        await _requestLogger.LogAsync(key!, "aip/fiscal-years", officeCode, null, (int)ok.StatusCode, cancellationToken);
        return ok;
    }
}
