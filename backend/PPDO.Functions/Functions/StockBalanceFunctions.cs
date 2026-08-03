using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Inventory;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for warehouse stock input (<c>/api/inventory/stock-balances</c>),
/// RAL-193. Every operation requires <c>CanAccessInventory</c> — enforced in
/// <see cref="IStockBalanceService"/>, not here (mirrors <c>ResourceLinkFunctions</c>).
/// </summary>
public sealed class StockBalanceFunctions
{
    private readonly IStockBalanceService _service;
    private readonly IJwtMiddleware       _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public StockBalanceFunctions(IStockBalanceService service, IJwtMiddleware jwt)
    {
        _service = service;
        _jwt     = jwt;
    }

    // ── GET /api/inventory/stock-balances?stockNo= ─────────────────────────────

    [Function("GetStockBalanceHistory")]
    public async Task<HttpResponseData> GetHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/stock-balances")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        string? stockNo = req.Query["stockNo"];
        if (string.IsNullOrWhiteSpace(stockNo))
            return await BadRequest(req, "stockNo query parameter is required.");

        ServiceResult<IReadOnlyList<StockBalanceDto>> result =
            await _service.GetHistoryAsync(caller, stockNo, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/inventory/stock-balances ─────────────────────────────────────

    [Function("CreateStockBalance")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/stock-balances")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreateStockBalanceDto? body = await DeserializeAsync<CreateStockBalanceDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<StockBalanceDto> result = await _service.CreateAsync(caller, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── PUT /api/inventory/stock-balances/{id} ─────────────────────────────────

    [Function("UpdateStockBalance")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "inventory/stock-balances/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        UpdateStockBalanceDto? body = await DeserializeAsync<UpdateStockBalanceDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<StockBalanceDto> result = await _service.UpdateAsync(caller, id, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── DELETE /api/inventory/stock-balances/{id} ──────────────────────────────

    [Function("DeleteStockBalance")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "inventory/stock-balances/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<StockBalanceDto> result = await _service.DeleteAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/inventory/stock-balances/import/preview ─────────────────────

    [Function("PreviewStockBalanceImport")]
    public async Task<HttpResponseData> PreviewImport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/stock-balances/import/preview")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // req.Body in the isolated worker is a non-seekable HttpRequestStream — ClosedXML's
        // internal .Length/seek calls throw on it. Copy to a MemoryStream first (same fix
        // documented in CLAUDE.md and used by PurchaseRequestFunctions.Import).
        using MemoryStream ms = new();
        await req.Body.CopyToAsync(ms, cancellationToken);

        if (ms.Length == 0)
            return await BadRequest(req, "Request body must contain the .xlsx file.");

        ms.Position = 0;

        ServiceResult<StockBalanceImportPreviewDto> result =
            await _service.PreviewImportAsync(caller, ms, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/inventory/stock-balances/import/commit ──────────────────────

    [Function("CommitStockBalanceImport")]
    public async Task<HttpResponseData> CommitImport(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "inventory/stock-balances/import/commit")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CommitStockBalanceImportDto? body =
            await DeserializeAsync<CommitStockBalanceImportDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<StockBalanceImportResultDto> result =
            await _service.CommitImportAsync(caller, body, cancellationToken);

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
            return await JsonSerializer.DeserializeAsync<T>(req.Body, _jsonOptions, cancellationToken);
        }
        catch { return default; }
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
            await ok.WriteStringAsync(JsonSerializer.Serialize(result.Value, _jsonOptions), cancellationToken);
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

        HttpResponseData error = req.CreateResponse(status);
        await error.WriteStringAsync(result.Error ?? "An unexpected error occurred.");
        return error;
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message);
        return response;
    }
}
