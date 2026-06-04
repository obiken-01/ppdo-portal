using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Distribution;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for the Distribution feature.
///
/// Endpoints:
///   GET  /api/distributions/item/{stockNo} — breakdown for one catalog item
///   POST /api/distributions                — create a single distribution
/// </summary>
public sealed class DistributionFunctions
{
    private readonly IDistributionService _service;
    private readonly IJwtMiddleware       _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DistributionFunctions(IDistributionService service, IJwtMiddleware jwt)
    {
        _service = service;
        _jwt     = jwt;
    }

    // ── GET /api/distributions/item/{stockNo} ─────────────────────────────────

    [Function("GetItemDistributionSummary")]
    public async Task<HttpResponseData> GetItemSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "distributions/item/{stockNo}")]
        HttpRequestData req,
        string stockNo,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<ItemDistributionSummaryDto> result =
            await _service.GetItemSummaryAsync(caller, stockNo, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/distributions ───────────────────────────────────────────────

    [Function("CreateDistribution")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "distributions")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreateStandaloneDistributionDto? body =
            await DeserializeAsync<CreateStandaloneDistributionDto>(req, cancellationToken);

        if (body is null)
            return await PlainError(req, HttpStatusCode.BadRequest,
                "Request body is missing or malformed.", cancellationToken);

        ServiceResult<DistributionCreatedDto> result =
            await _service.CreateAsync(caller, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? vals)
            ? vals.FirstOrDefault()
            : null;

    private static async Task<T?> DeserializeAsync<T>(
        HttpRequestData req, CancellationToken cancellationToken)
    {
        try { return await JsonSerializer.DeserializeAsync<T>(req.Body, _jsonOptions, cancellationToken); }
        catch { return default; }
    }

    private static async Task<HttpResponseData> OkJson<T>(
        HttpRequestData req, T body, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, _jsonOptions), cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> ToResponse<T>(
        HttpRequestData req, ServiceResult<T> result,
        HttpStatusCode successStatus, CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            HttpResponseData ok = req.CreateResponse(successStatus);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(JsonSerializer.Serialize(result.Value, _jsonOptions), cancellationToken);
            return ok;
        }

        HttpStatusCode status = result.Code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.Forbidden  => HttpStatusCode.Forbidden,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            ServiceErrorCode.Conflict   => HttpStatusCode.Conflict,
            _                           => HttpStatusCode.InternalServerError,
        };
        return await PlainError(req, status, result.Error!, cancellationToken);
    }

    private static async Task<HttpResponseData> PlainError(
        HttpRequestData req, HttpStatusCode status, string message,
        CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(status);
        await response.WriteStringAsync(message, cancellationToken);
        return response;
    }
}
