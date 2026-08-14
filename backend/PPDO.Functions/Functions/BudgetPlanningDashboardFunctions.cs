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
    //
    // PPDO-only (RAL-230): the payload IS PPDO's — GetDashboardAsync resolves the PPDO office
    // internally and returns its ceilings, per-division allocations, and per-division WFP
    // status. There is no office dimension to clamp, so a non-PPDO caller is refused outright
    // rather than served someone else's data. Office users get the office readiness hub via
    // GetOfficeDashboard, and their fiscal-year list via GetFiscalYears.

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

        // Generic 403 — don't confirm to an office user whether PPDO data exists.
        if (!OfficeScope.Resolve(caller).SeeAll)
            return req.CreateResponse(HttpStatusCode.Forbidden);

        int? fiscalYear = TryParseIntQuery(req, "fiscalYear");
        int? divisionId = await _permissions.CanManageAllocationAsync(caller, cancellationToken)
            ? null
            : caller.DivisionId;

        PpdoDashboardDto result =
            await _service.GetDashboardAsync(fiscalYear, divisionId, cancellationToken);

        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/budget-planning/fiscal-years?fiscalYear={int?} ──────────────
    // RAL-166 follow-up: the fiscal-year picker alone, for callers (the Report page) that
    // don't need the rest of the Dashboard payload — same auth/permission gate as GetDashboard.

    [Function("GetBudgetPlanningFiscalYears")]
    public async Task<HttpResponseData> GetFiscalYears(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/fiscal-years")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanAccessBudgetPlanningAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        int? fiscalYear = TryParseIntQuery(req, "fiscalYear");

        FiscalYearsDto result = await _service.GetFiscalYearsAsync(fiscalYear, cancellationToken);

        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/budget-planning/activity?officeId={int?} ────────────────────
    // officeId (RAL-229): office-scoped callers are ALWAYS clamped to their own office — an
    // officeId they put on the query string is ignored, and omitting it does NOT widen them to
    // all offices. PPDO callers pass through unchanged (including null = every office).
    // Same rule as GetOfficeDashboard below; mirrors GetDashboard's RAL-161 division clamp.

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

        int? officeId = ConfigHttp.ClampOfficeId(caller, TryParseIntQuery(req, "officeId"));

        IReadOnlyList<RecentActivityDto> result =
            await _service.GetRecentActivityAsync(officeId, cancellationToken);

        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/budget-planning/dashboard/office?officeId=&fiscalYear= ──────
    // officeId (RAL-229): office-scoped callers are ALWAYS clamped to their own office — the
    // officeId on the query string is ignored for them. Before this, the caller was discarded
    // entirely (`(_, denied)`) and any Budget Planning user could read any office's dashboard
    // by changing one parameter. Clamp, don't reject: no error path to get wrong, and a client
    // can't probe for valid office ids by watching which ones 403.

    [Function("GetBudgetPlanningOfficeDashboard")]
    public async Task<HttpResponseData> GetOfficeDashboard(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/dashboard/office")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(
            req, _jwt, u => _permissions.CanAccessBudgetPlanningAsync(u), cancellationToken);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<OfficeDashboardDto>.Fail(
                    "officeId and fiscalYear query parameters are required."), cancellationToken);

        // Clamp AFTER validation so a malformed officeId is still a clean 400 rather than
        // silently falling back to the caller's own office.
        officeId = ConfigHttp.ClampOfficeId(caller!, officeId) ?? officeId;

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
