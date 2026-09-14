using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// The in-app notifications read (V18-58 / PPDO-75, <c>AIP_Review_Spec.md</c> §4, §6.5).
///
/// <para>
/// ⚠️ <b>Gated on <c>CanAccessBudgetPlanning</c>, not on a reviewer flag.</b> An encoder has no count
/// but does get the returned notice, and it rides this same request. What each caller receives is the
/// service's question, answered from their own flags and office — the route takes no parameters.
/// </para>
/// </summary>
public sealed class AipNotificationFunctions
{
    private readonly IAipNotificationService _notifications;
    private readonly IJwtMiddleware          _jwt;
    private readonly IPermissionService      _permissions;

    public AipNotificationFunctions(
        IAipNotificationService notifications,
        IJwtMiddleware          jwt,
        IPermissionService      permissions)
    {
        _notifications = notifications;
        _jwt           = jwt;
        _permissions   = permissions;
    }

    private Task<bool> CanAccessBudgetPlanning(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/aip/review/notifications ─────────────────────
    //
    // ⚠️ Route ordering: "review" is not an int, so this cannot collide with
    // budget-planning/aip/{aipId:int}.
    [Function("AipReviewNotifications")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/review/notifications")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _notifications.GetForCallerAsync(caller!, ct), ct);
    }
}
