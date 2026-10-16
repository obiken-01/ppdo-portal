using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// The notification bell's feed (v1.9.0 — PPDO-221, <c>Notification_Centre_Spec.md</c> §4).
///
/// <para>
/// ⚠️ <b>Any signed-in user, and only their own rows.</b> No route takes a user id: the service reads
/// the caller off the token and every query filters on it, so another user's notification id answers
/// 404 exactly like a missing one. These are not budget-planning content writes, so
/// <c>AuthorizeWriteAsync</c> (the reviewer guard) does not apply; marking your own entry read is not
/// a content write.
/// </para>
/// </summary>
public sealed class NotificationFunctions
{
    private readonly INotificationService _notifications;
    private readonly IJwtValidator        _jwt;

    public NotificationFunctions(INotificationService notifications, IJwtValidator jwt)
    {
        _notifications = notifications;
        _jwt           = jwt;
    }

    // ── GET /api/notifications/unread-count ───────────────────────────────────
    [Function("NotificationUnreadCount")]
    public async Task<HttpResponseData> UnreadCount(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications/unread-count")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _notifications.GetUnreadCountAsync(caller!, ct), ct);
    }

    // ── GET /api/notifications?unreadOnly=&page=&pageSize= ────────────────────
    [Function("NotificationList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "notifications")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        System.Collections.Specialized.NameValueCollection query = HttpUtility.ParseQueryString(req.Url.Query);
        bool unreadOnly = bool.TryParse(query["unreadOnly"], out bool u) && u;
        int  page       = int.TryParse(query["page"], out int p) ? p : 1;
        int  pageSize   = int.TryParse(query["pageSize"], out int s) ? s : NotificationService.DefaultPageSize;

        return await ConfigHttp.FromResultAsync(req,
            await _notifications.ListAsync(caller!, unreadOnly, page, pageSize, ct), ct);
    }

    // ── POST /api/notifications/{id}/read ─────────────────────────────────────
    [Function("NotificationMarkRead")]
    public async Task<HttpResponseData> MarkRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/{id:long}/read")] HttpRequestData req,
        long id,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        ServiceResult<bool> result = await _notifications.MarkReadAsync(caller!, id, ct);
        return result.IsSuccess
            ? req.CreateResponse(HttpStatusCode.NoContent)
            : await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── POST /api/notifications/read-all ──────────────────────────────────────
    [Function("NotificationMarkAllRead")]
    public async Task<HttpResponseData> MarkAllRead(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "notifications/read-all")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        ServiceResult<bool> result = await _notifications.MarkAllReadAsync(caller!, ct);
        return result.IsSuccess
            ? req.CreateResponse(HttpStatusCode.NoContent)
            : await ConfigHttp.FromResultAsync(req, result, ct);
    }
}
