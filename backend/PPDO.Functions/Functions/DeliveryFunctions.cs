using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Delivery;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for Deliveries (<c>/api/deliveries</c>).
///
/// All endpoints require a valid JWT. Division-scope and permission enforcement
/// are delegated entirely to <see cref="IDeliveryService"/>.
///
/// Endpoints:
///   GET  /api/deliveries             — list all deliveries (division-scoped for Staff/Observer)
///   GET  /api/deliveries/{id}        — delivery detail with items and distributions
///   GET  /api/deliveries?prId={id}   — list deliveries for a specific PR
///   POST /api/deliveries             — submit a delivery, triggers PR status update
/// </summary>
public sealed class DeliveryFunctions
{
    private readonly IDeliveryService _service;
    private readonly IJwtMiddleware   _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DeliveryFunctions(IDeliveryService service, IJwtMiddleware jwt)
    {
        _service = service;
        _jwt     = jwt;
    }

    // ── GET /api/deliveries  or  GET /api/deliveries?prId={guid} ─────────────

    [Function("GetDeliveries")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "deliveries")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // Optional ?prId= filter — returns deliveries for a specific PR.
        string? prIdParam = req.Query["prId"];
        if (!string.IsNullOrEmpty(prIdParam))
        {
            if (!Guid.TryParse(prIdParam, out Guid prId))
                return await PlainError(req, HttpStatusCode.BadRequest,
                    "Invalid prId — must be a valid GUID.", cancellationToken);

            ServiceResult<IReadOnlyList<DeliverySummaryDto>> byPR =
                await _service.GetByPRIdAsync(caller, prId, cancellationToken);
            return await ToResponse(req, byPR, HttpStatusCode.OK, cancellationToken);
        }

        IReadOnlyList<DeliverySummaryDto> result =
            await _service.GetAllAsync(caller, cancellationToken);
        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/deliveries/{id} ──────────────────────────────────────────────

    [Function("GetDeliveryById")]
    public async Task<HttpResponseData> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "deliveries/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<DeliveryResponseDto> result =
            await _service.GetByIdAsync(caller, id, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/deliveries ──────────────────────────────────────────────────

    [Function("CreateDelivery")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "deliveries")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreateDeliveryDto? body = await DeserializeAsync<CreateDeliveryDto>(req, cancellationToken);
        if (body is null)
            return await PlainError(req, HttpStatusCode.BadRequest,
                "Request body is missing or malformed.", cancellationToken);

        ServiceResult<DeliveryResponseDto> result =
            await _service.CreateAsync(caller, body, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static async Task<T?> DeserializeAsync<T>(
        HttpRequestData req, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                req.Body, _jsonOptions, cancellationToken);
        }
        catch { return default; }
    }

    private static async Task<HttpResponseData> OkJson<T>(
        HttpRequestData req, T body, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(body, _jsonOptions), cancellationToken);
        return response;
    }

    private static async Task<HttpResponseData> ToResponse<T>(
        HttpRequestData req,
        ServiceResult<T> result,
        HttpStatusCode successStatus,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            HttpResponseData ok = req.CreateResponse(successStatus);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(
                JsonSerializer.Serialize(result.Value, _jsonOptions), cancellationToken);
            return ok;
        }

        HttpStatusCode status = result.Code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.Forbidden  => HttpStatusCode.Forbidden,
            ServiceErrorCode.Conflict   => HttpStatusCode.Conflict,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            _                           => HttpStatusCode.InternalServerError,
        };
        return await PlainError(req, status, result.Error!, cancellationToken);
    }

    private static async Task<HttpResponseData> PlainError(
        HttpRequestData req,
        HttpStatusCode status,
        string message,
        CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(status);
        await response.WriteStringAsync(message, cancellationToken);
        return response;
    }
}
