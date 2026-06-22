using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Announcements;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for announcements (<c>/api/announcements</c>).
///
/// Public endpoint (no JWT required):
///   GET /api/announcements — published announcements for the public landing page
///
/// Admin/SuperAdmin endpoints (JWT validated):
///   GET    /api/announcements/manage
///   POST   /api/announcements
///   PUT    /api/announcements/{id}
///   PUT    /api/announcements/{id}/publish
///   PUT    /api/announcements/{id}/unpublish
///   PUT    /api/announcements/{id}/archive
///   DELETE /api/announcements/{id}
///
/// All triggers use AuthorizationLevel.Anonymous — JWT is validated manually per
/// CLAUDE.md architecture rules. Business logic lives in AnnouncementService.
/// </summary>
public sealed class AnnouncementFunctions
{
    private readonly IAnnouncementService _announcements;
    private readonly IJwtMiddleware _jwt;

    public AnnouncementFunctions(IAnnouncementService announcements, IJwtMiddleware jwt)
    {
        _announcements = announcements;
        _jwt           = jwt;
    }

    // ── GET /api/announcements (PUBLIC — no JWT) ──────────────────────────────

    [Function("GetPublicAnnouncements")]
    public async Task<HttpResponseData> GetPublic(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "announcements")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AnnouncementPublicDto> items =
            await _announcements.GetPublishedAsync(cancellationToken);

        return await OkJson(req, items, cancellationToken);
    }

    // ── GET /api/announcements/manage ─────────────────────────────────────────

    [Function("GetManageAnnouncements")]
    public async Task<HttpResponseData> GetManage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "announcements/manage")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<IReadOnlyList<AnnouncementDto>> result =
            await _announcements.GetAllAsync(caller, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/announcements ────────────────────────────────────────────────

    [Function("CreateAnnouncement")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "announcements")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreateAnnouncementDto? body = await DeserializeAsync<CreateAnnouncementDto>(req, cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.Title))
            return await BadRequest(req, "Title and Content are required.");

        ServiceResult<AnnouncementDto> result =
            await _announcements.CreateAsync(caller, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── PUT /api/announcements/{id} ────────────────────────────────────────────

    [Function("UpdateAnnouncement")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "announcements/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        UpdateAnnouncementDto? body = await DeserializeAsync<UpdateAnnouncementDto>(req, cancellationToken);
        if (body is null || string.IsNullOrWhiteSpace(body.Title))
            return await BadRequest(req, "Title and Content are required.");

        ServiceResult<AnnouncementDto> result =
            await _announcements.UpdateAsync(caller, id, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/announcements/{id}/publish ────────────────────────────────────

    [Function("PublishAnnouncement")]
    public async Task<HttpResponseData> Publish(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "announcements/{id:guid}/publish")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<AnnouncementDto> result =
            await _announcements.PublishAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/announcements/{id}/unpublish ──────────────────────────────────

    [Function("UnpublishAnnouncement")]
    public async Task<HttpResponseData> Unpublish(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "announcements/{id:guid}/unpublish")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<AnnouncementDto> result =
            await _announcements.UnpublishAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/announcements/{id}/archive ────────────────────────────────────

    [Function("ArchiveAnnouncement")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "announcements/{id:guid}/archive")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<AnnouncementDto> result =
            await _announcements.ArchiveAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── DELETE /api/announcements/{id} ────────────────────────────────────────

    [Function("DeleteAnnouncement")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "announcements/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<bool> result =
            await _announcements.DeleteAsync(caller, id, cancellationToken);

        if (!result.IsSuccess)
            return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    };

    private static async Task<T?> DeserializeAsync<T>(
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(
                req.Body, _jsonOptions, cancellationToken);
        }
        catch
        {
            return default;
        }
    }

    private static async Task<HttpResponseData> OkJson<T>(
        HttpRequestData req,
        T body,
        CancellationToken cancellationToken)
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

    private static async Task<HttpResponseData> BadRequest(
        HttpRequestData req,
        string message)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message);
        return response;
    }
}
