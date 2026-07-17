using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for the Budget Planning Dashboard (RAL-80, RAL-60).
/// All endpoints require a valid JWT with CanAccessBudgetPlanning — no public access.
/// GetDashboard/GetActivity return raw JSON (no envelope), same as DashboardFunctions.
/// GetOfficeDashboard (RAL-60) uses the { data, error, message } envelope per its ticket.
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
    // divisionId (RAL-161): division-scoped callers (not CanManageAllocation) are ALWAYS
    // clamped to their own division — mirrors WfpReportFunctions.GetPreview's RAL-136 pattern.
    // There is no client-supplied divisionId param here at all; a division-scoped caller can
    // never see another division's data by any query string.

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
        int? divisionId = await _permissions.CanManageAllocationAsync(caller, cancellationToken)
            ? null
            : caller.DivisionId;

        PpdoDashboardDto result =
            await _service.GetDashboardAsync(fiscalYear, divisionId, cancellationToken);

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

    // ── GET /api/budget-planning/dashboard/office?officeId=&fiscalYear= ──────

    [Function("GetBudgetPlanningOfficeDashboard")]
    public async Task<HttpResponseData> GetOfficeDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/dashboard/office")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(
            req, _jwt, u => _permissions.CanAccessBudgetPlanningAsync(u), cancellationToken);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<OfficeDashboardDto>.Fail(
                    "officeId and fiscalYear query parameters are required."), cancellationToken);

        OfficeDashboardDto result =
            await _service.GetOfficeDashboardAsync(officeId, fiscalYear, cancellationToken);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<OfficeDashboardDto>.Ok(result), cancellationToken);
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
