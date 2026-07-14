// Dev/maintenance endpoint (wipes ALL LDIP/AIP/WFP records). INTENTIONALLY KEPT — gated by
// the same DevCleanupKey guard as the inventory cleanup endpoint; not a merge-blocker.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;

namespace PPDO.Functions.Functions;

/// <summary>
/// Dev/maintenance cleanup endpoints for wiping budget planning records.
/// Trigger via Postman; no UI. Guarded by a single safeguard:
///   - X-Dev-Cleanup-Key header matching the DevCleanupKey config value.
/// If DevCleanupKey is absent or empty in config, the endpoint returns 403 immediately
/// (safe-fail — it is never set in Azure production App Settings; set it temporarily there
/// only for a controlled live-testing window, per RAL-137).
/// </summary>
public sealed class BudgetPlanningCleanupFunctions
{
    private readonly ILdipService       _ldip;
    private readonly IAipService        _aip;
    private readonly IWfpService        _wfp;
    private readonly IConfiguration     _config;

    public BudgetPlanningCleanupFunctions(
        ILdipService     ldip,
        IAipService      aip,
        IWfpService      wfp,
        IConfiguration   config)
    {
        _ldip   = ldip;
        _aip    = aip;
        _wfp    = wfp;
        _config = config;
    }

    // ── DELETE /api/budget-planning/cleanup ───────────────────────────────────
    [Function("BudgetPlanningCleanup")]
    public async Task<HttpResponseData> Cleanup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/cleanup")] HttpRequestData req,
        CancellationToken ct)
    {
        HttpResponseData? denied = await CheckDevCleanupKeyAsync(req, ct);
        if (denied is not null) return denied;

        // Delete in FK-safe order: WFP → AIP hierarchy → LDIP.
        // DB cascade removes WfpActivities/Lines when WfpRecord is deleted.
        // AIP cascade removes office/program/project/activity when AipRecord is deleted.
        int wfpCount  = await _wfp.PurgeAllAsync(ct);
        int aipCount  = await _aip.PurgeAllAsync(ct);
        int ldipCount = await _ldip.PurgeAllAsync(ct);

        object data = new { wfpRecords = wfpCount, aipRecords = aipCount, ldipRecords = ldipCount };
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<object>.Ok(data, "Budget planning records purged."), ct);
    }

    // ── DELETE /api/budget-planning/wfp/cleanup?officeId=&fiscalYear=&divisionId= ─────────────
    // Scoped sibling of BudgetPlanningCleanup (RAL-137): resets ONE office+division+FY's WFP
    // record instead of wiping every AIP/LDIP/WFP record — for resetting a single test scenario
    // during live testing without nuking the whole environment. Same DevCleanupKey guard.
    // Unconditional like PurgeAllAsync: deletes Draft or Final records alike.
    [Function("WfpCleanupScoped")]
    public async Task<HttpResponseData> CleanupScoped(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete",
            Route = "budget-planning/wfp/cleanup")] HttpRequestData req,
        CancellationToken ct)
    {
        HttpResponseData? denied = await CheckDevCleanupKeyAsync(req, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpCleanupResultDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? divisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;

        WfpCleanupResultDto? result = await _wfp.CleanupScopedAsync(officeId, divisionId, fiscalYear, ct);
        if (result is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                ApiResponse<WfpCleanupResultDto>.Fail(
                    $"No WFP record found for office {officeId}, division {(divisionId?.ToString() ?? "none")}, FY {fiscalYear}."), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<WfpCleanupResultDto>.Ok(result, "WFP record and its expenditures purged."), ct);
    }

    /// <summary>
    /// Shared guard for both cleanup endpoints: the DevCleanupKey config value must be set (never
    /// set in Azure production App Settings — safe-fail) and match the request header exactly.
    /// Returns a 403 response to short-circuit on failure, or null to proceed.
    /// </summary>
    private async Task<HttpResponseData?> CheckDevCleanupKeyAsync(HttpRequestData req, CancellationToken ct)
    {
        string? devKey = _config["DevCleanupKey"];
        if (string.IsNullOrWhiteSpace(devKey))
            return await Forbidden(req, "Cleanup endpoint is not available in this environment.", ct);

        string? headerKey = req.Headers.TryGetValues("X-Dev-Cleanup-Key", out IEnumerable<string>? vals)
            ? vals.FirstOrDefault()
            : null;

        return headerKey == devKey ? null : await Forbidden(req, "Invalid or missing X-Dev-Cleanup-Key header.", ct);
    }

    private static Task<HttpResponseData> Forbidden(HttpRequestData req, string message, CancellationToken ct) =>
        ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden, ApiResponse<object>.Fail(message), ct);
}
