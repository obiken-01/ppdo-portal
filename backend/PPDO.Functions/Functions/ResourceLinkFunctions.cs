using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.ResourceLinks;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for Resource Links (<c>/api/resource-links</c>).
///
/// Permission summary (enforced in <see cref="IResourceLinkService"/>):
///   GET    — all authenticated roles
///   POST   — Admin/SuperAdmin OR Staff with CanManageResourceLinks
///   PUT    — Admin/SuperAdmin only
///   DELETE — Admin/SuperAdmin only
/// </summary>
public sealed class ResourceLinkFunctions
{
    private readonly IResourceLinkService _service;
    private readonly IJwtMiddleware       _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ResourceLinkFunctions(IResourceLinkService service, IJwtMiddleware jwt)
    {
        _service = service;
        _jwt     = jwt;
    }

    // ── GET /api/resource-links ────────────────────────────────────────────────

    [Function("GetResourceLinks")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "resource-links")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        IReadOnlyList<ResourceLinkCategoryDto> result =
            await _service.GetAllAsync(cancellationToken);

        return await OkJson(req, result, cancellationToken);
    }

    // ── POST /api/resource-links ───────────────────────────────────────────────

    [Function("CreateResourceLink")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "resource-links")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreateResourceLinkDto? body =
            await DeserializeAsync<CreateResourceLinkDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<ResourceLinkDto> result =
            await _service.CreateAsync(caller, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── PUT /api/resource-links/{id} ───────────────────────────────────────────

    [Function("UpdateResourceLink")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "resource-links/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        UpdateResourceLinkDto? body =
            await DeserializeAsync<UpdateResourceLinkDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<ResourceLinkDto> result =
            await _service.UpdateAsync(caller, id, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── DELETE /api/resource-links/{id} ───────────────────────────────────────

    [Function("DeleteResourceLink")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "resource-links/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<ResourceLinkDto> result =
            await _service.DeleteAsync(caller, id, cancellationToken);

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
