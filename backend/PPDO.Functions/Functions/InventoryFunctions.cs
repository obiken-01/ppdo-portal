using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.DTOs.Inventory;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for the Inventory Dashboard and Item Ledger.
///
/// All endpoints require a valid JWT and CanAccessInventory permission.
/// Division scope is enforced inside <see cref="IInventoryService"/>.
///
/// Endpoints:
///   GET /api/inventory/stats   — two grouped stat panels (PR counts + alerts)
///   GET /api/inventory/ledger  — per-item running stock totals
/// </summary>
public sealed class InventoryFunctions
{
    private readonly IInventoryService _service;
    private readonly IJwtMiddleware    _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public InventoryFunctions(IInventoryService service, IJwtMiddleware jwt)
    {
        _service = service;
        _jwt     = jwt;
    }

    // ── GET /api/inventory/stats ──────────────────────────────────────────────

    [Function("GetInventoryStats")]
    public async Task<HttpResponseData> GetStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/stats")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        InventoryStatsDto result =
            await _service.GetStatsAsync(caller, cancellationToken);
        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/inventory/ledger ─────────────────────────────────────────────

    [Function("GetItemLedger")]
    public async Task<HttpResponseData> GetLedger(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "inventory/ledger")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        IReadOnlyList<ItemLedgerRowDto> result =
            await _service.GetItemLedgerAsync(caller, cancellationToken);
        return await OkJson(req, result, cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static async Task<HttpResponseData> OkJson<T>(
        HttpRequestData req, T body, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(body, _jsonOptions), cancellationToken);
        return response;
    }
}
