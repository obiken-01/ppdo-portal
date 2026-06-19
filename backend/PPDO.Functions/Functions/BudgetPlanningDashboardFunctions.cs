using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for the Budget Planning Dashboard (RAL-80).
/// All endpoints require a valid JWT with CanAccessBudgetPlanning — no public access.
/// Responses are raw JSON (no { data, error, message } envelope), same as DashboardFunctions.
/// </summary>
public sealed class BudgetPlanningDashboardFunctions
{
    private readonly IBudgetPlanningDashboardService _service;
    private readonly IJwtMiddleware                  _jwt;
    private readonly IPermissionService              _permissions;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public BudgetPlanningDashboardFunctions(
        IBudgetPlanningDashboardService service,
        IJwtMiddleware                  jwt,
        IPermissionService              permissions)
    {
        _service     = service;
        _jwt         = jwt;
        _permissions = permissions;
    }

    // ── GET /api/budget-planning/dashboard?fiscalYear={int?} ──────────────────

    [Function("GetBudgetPlanningDashboard")]
    public async Task<HttpResponseData> GetDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/dashboard")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanAccessBudgetPlanningAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        int? fiscalYear = TryParseIntQuery(req, "fiscalYear");

        PlanningDashboardDto result =
            await _service.GetDashboardAsync(fiscalYear, cancellationToken);

        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/budget-planning/activity?officeId={int?} ────────────────────

    [Function("GetBudgetPlanningActivity")]
    public async Task<HttpResponseData> GetActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/activity")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanAccessBudgetPlanningAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        int? officeId = TryParseIntQuery(req, "officeId");

        IReadOnlyList<RecentActivityDto> result =
            await _service.GetRecentActivityAsync(officeId, cancellationToken);

        return await OkJson(req, result, cancellationToken);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static int? TryParseIntQuery(HttpRequestData req, string key)
    {
        string? raw = req.Query[key];
        return int.TryParse(raw, out int value) ? value : null;
    }

    private static async Task<HttpResponseData> OkJson<T>(
        HttpRequestData req, T body, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, JsonOptions), cancellationToken);
        return response;
    }
}
