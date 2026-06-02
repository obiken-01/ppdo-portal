using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Items;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for Items Master (<c>/api/items</c>).
///
/// All endpoints require a valid JWT.
/// Write operations additionally require CanAccessInventory — enforced in <see cref="IItemService"/>.
///
/// Endpoints:
///   GET  /api/items/master           — full catalog
///   GET  /api/items/master/{id}      — single item
///   POST /api/items/master           — add item (CanAccessInventory)
///   PUT  /api/items/master/{id}      — update item (CanAccessInventory)
///   GET  /api/items/lookup?term=     — bidirectional autocomplete (all authenticated)
/// </summary>
public sealed class ItemFunctions
{
    private readonly IItemService  _items;
    private readonly IJwtMiddleware _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ItemFunctions(IItemService items, IJwtMiddleware jwt)
    {
        _items = items;
        _jwt   = jwt;
    }

    // ── GET /api/items/master ─────────────────────────────────────────────────

    [Function("GetItemsMaster")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "items/master")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        IReadOnlyList<ItemMasterDto> result = await _items.GetAllAsync(cancellationToken);
        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/items/master/{id} ────────────────────────────────────────────

    [Function("GetItemMasterById")]
    public async Task<HttpResponseData> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "items/master/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<ItemMasterDto> result = await _items.GetByIdAsync(id, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/items/master ────────────────────────────────────────────────

    [Function("CreateItemMaster")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "items/master")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreateItemMasterDto? body = await DeserializeAsync<CreateItemMasterDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<ItemMasterDto> result =
            await _items.CreateAsync(caller, body, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── PUT /api/items/master/{id} ────────────────────────────────────────────

    [Function("UpdateItemMaster")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "items/master/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        UpdateItemMasterDto? body = await DeserializeAsync<UpdateItemMasterDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<ItemMasterDto> result =
            await _items.UpdateAsync(caller, id, body, cancellationToken);
        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── GET /api/items/lookup?term= ───────────────────────────────────────────

    [Function("LookupItems")]
    public async Task<HttpResponseData> Lookup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "items/lookup")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        string term = req.Query["term"] ?? string.Empty;
        IReadOnlyList<ItemLookupDto> result =
            await _items.LookupAsync(term, cancellationToken);
        return await OkJson(req, result, cancellationToken);
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
