using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Dashboard;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for the Main Dashboard (<c>/api/dashboard</c>).
/// All endpoints require a valid JWT — no public access.
/// Calendar approval endpoints added in v1.1.1 (RAL-84).
/// </summary>
public sealed class DashboardFunctions
{
    private readonly IDashboardService _dashboard;
    private readonly IJwtMiddleware    _jwt;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public DashboardFunctions(IDashboardService dashboard, IJwtMiddleware jwt)
    {
        _dashboard = dashboard;
        _jwt       = jwt;
    }

    // ── GET /api/dashboard/events ──────────────────────────────────────────────
    // Query params: ?year=2026&month=6  (defaults to current Manila month if omitted)

    [Function("GetDashboardEvents")]
    public async Task<HttpResponseData> GetEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/events")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // Parse year/month from query string; fall back to current Manila time (UTC+8).
        DateTime manilaNow = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"));

        int year  = TryParseQuery(req, "year",  manilaNow.Year);
        int month = TryParseQuery(req, "month", manilaNow.Month);

        if (month < 1 || month > 12)
            return await BadRequest(req, "month must be between 1 and 12.");

        IReadOnlyList<CalendarEventDto> events =
            await _dashboard.GetEventsAsync(year, month, caller.Id, cancellationToken);

        return await OkJson(req, events, cancellationToken);
    }

    // ── POST /api/dashboard/events ─────────────────────────────────────────────

    [Function("CreateDashboardEvent")]
    public async Task<HttpResponseData> CreateEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "dashboard/events")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        CreateCalendarEventDto? body =
            await DeserializeAsync<CreateCalendarEventDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<CalendarEventDto> result =
            await _dashboard.CreateEventAsync(caller, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── GET /api/dashboard/events/pending ─────────────────────────────────────

    [Function("GetPendingEvents")]
    public async Task<HttpResponseData> GetPendingEvents(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/events/pending")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<IReadOnlyList<PendingCalendarEventDto>> result =
            await _dashboard.GetPendingEventsAsync(caller, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/dashboard/events/{id}/review ─────────────────────────────────

    [Function("ReviewCalendarEvent")]
    public async Task<HttpResponseData> ReviewEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "dashboard/events/{id:guid}/review")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ReviewCalendarEventDto? body =
            await DeserializeAsync<ReviewCalendarEventDto>(req, cancellationToken);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<CalendarEventDto> result =
            await _dashboard.ReviewEventAsync(caller, id, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── DELETE /api/dashboard/events/{id} ─────────────────────────────────────

    [Function("DeleteCalendarEvent")]
    public async Task<HttpResponseData> DeleteEvent(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "dashboard/events/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<bool> result =
            await _dashboard.DeleteEventAsync(caller, id, cancellationToken);

        if (!result.IsSuccess)
            return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── GET /api/dashboard/stats ───────────────────────────────────────────────

    [Function("GetDashboardStats")]
    public async Task<HttpResponseData> GetStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "dashboard/stats")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        DashboardStatsDto stats = await _dashboard.GetStatsAsync(cancellationToken);
        return await OkJson(req, stats, cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static int TryParseQuery(HttpRequestData req, string key, int fallback)
    {
        string? raw = req.Query[key];
        return int.TryParse(raw, out int value) ? value : fallback;
    }

    private static async Task<T?> DeserializeAsync<T>(
        HttpRequestData req, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer.DeserializeAsync<T>(req.Body, _jsonOptions, cancellationToken);
        }
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
