using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.PurchaseRequest;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for Purchase Requests (<c>/api/purchase-requests</c>).
///
/// All endpoints require a valid JWT. Division-scope and permission enforcement
/// are delegated entirely to <see cref="IPurchaseRequestService"/>.
///
/// Endpoints:
///   GET  /api/purchase-requests             — list PRs (division-scoped for Staff/Observer)
///   GET  /api/purchase-requests/{id}        — PR detail with line items
///   POST /api/purchase-requests             — submit new PR
///   PUT  /api/purchase-requests/{id}        — update PR (Admin only, status = Open)
///   GET  /api/purchase-requests/template    — download blank PR import template (.xlsx)
///   POST /api/purchase-requests/import      — upload populated PR template → create PRs
/// </summary>
public sealed class PurchaseRequestFunctions
{
    private readonly IPurchaseRequestService _service;
    private readonly IJwtMiddleware          _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PurchaseRequestFunctions(IPurchaseRequestService service, IJwtMiddleware jwt)
    {
        _service = service;
        _jwt     = jwt;
    }

    // ── GET /api/purchase-requests ────────────────────────────────────────────

    [Function("GetPurchaseRequests")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "purchase-requests")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        IReadOnlyList<PRSummaryDto> result =
            await _service.GetAllAsync(caller, cancellationToken);
        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/purchase-requests/template ───────────────────────────────────
    // Route must be declared before /{id} so the literal segment wins.

    [Function("GetPRTemplate")]
    public async Task<HttpResponseData> GetTemplate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "purchase-requests/template")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<byte[]> result =
            await _service.GetTemplateAsync(caller, cancellationToken);

        if (!result.IsSuccess)
            return await ErrorResponse(req, result.Code, result.Error!, cancellationToken);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            "attachment; filename=\"PR_Import_Template.xlsx\"");
        await response.WriteBytesAsync(result.Value!);
        return response;
    }

    // ── POST /api/purchase-requests/import ────────────────────────────────────

    [Function("ImportPurchaseRequests")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "purchase-requests/import")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (req.Body is null || req.Body.Length == 0)
            return await PlainError(req, HttpStatusCode.BadRequest,
                "Request body must contain the .xlsx file.", cancellationToken);

        ServiceResult<IReadOnlyList<PRResponseDto>> result =
            await _service.ImportFromExcelAsync(caller, req.Body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── GET /api/purchase-requests/{id} ──────────────────────────────────────

    [Function("GetPurchaseRequestById")]
    public async Task<HttpResponseData> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "purchase-requests/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<PRResponseDto> result =
            await _service.GetByIdAsync(caller, id, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/purchase-requests ───────────────────────────────────────────

    [Function("CreatePurchaseRequest")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "purchase-requests")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreatePRDto? body = await DeserializeAsync<CreatePRDto>(req, cancellationToken);
        if (body is null)
            return await PlainError(req, HttpStatusCode.BadRequest,
                "Request body is missing or malformed.", cancellationToken);

        ServiceResult<PRResponseDto> result =
            await _service.CreateAsync(caller, body, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── PUT /api/purchase-requests/{id} ──────────────────────────────────────

    [Function("UpdatePurchaseRequest")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "purchase-requests/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        UpdatePRDto? body = await DeserializeAsync<UpdatePRDto>(req, cancellationToken);
        if (body is null)
            return await PlainError(req, HttpStatusCode.BadRequest,
                "Request body is missing or malformed.", cancellationToken);

        ServiceResult<PRResponseDto> result =
            await _service.UpdateAsync(caller, id, body, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
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

        return await ErrorResponse(req, result.Code, result.Error!, cancellationToken);
    }

    private static async Task<HttpResponseData> ErrorResponse(
        HttpRequestData req,
        ServiceErrorCode code,
        string message,
        CancellationToken cancellationToken)
    {
        HttpStatusCode status = code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.Forbidden  => HttpStatusCode.Forbidden,
            ServiceErrorCode.Conflict   => HttpStatusCode.Conflict,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            _                           => HttpStatusCode.InternalServerError,
        };
        return await PlainError(req, status, message, cancellationToken);
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
