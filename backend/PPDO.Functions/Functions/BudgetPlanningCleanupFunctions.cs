// Dev/maintenance endpoint (wipes ALL LDIP/AIP/WFP records). INTENTIONALLY KEPT — gated by
// the same DevCleanupKey guard as the inventory cleanup endpoint; not a merge-blocker.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using PPDO.Application.Common;
using PPDO.Application.Services;

namespace PPDO.Functions.Functions;

/// <summary>
/// Dev/maintenance cleanup endpoint for wiping all budget planning records.
/// Trigger via Postman; no UI. Guarded by a single safeguard:
///   - X-Dev-Cleanup-Key header matching the DevCleanupKey config value.
/// If DevCleanupKey is absent or empty in config, the endpoint returns 403 immediately
/// (safe-fail — it is never set in Azure production App Settings).
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
        // Safeguard 1: Dev key must be configured and match the request header.
        string? devKey = _config["DevCleanupKey"];
        if (string.IsNullOrWhiteSpace(devKey))
            return await Forbidden(req, "Cleanup endpoint is not available in this environment.", ct);

        string? headerKey = req.Headers.TryGetValues("X-Dev-Cleanup-Key", out IEnumerable<string>? vals)
            ? vals.FirstOrDefault()
            : null;

        if (headerKey != devKey)
            return await Forbidden(req, "Invalid or missing X-Dev-Cleanup-Key header.", ct);

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

    private static Task<HttpResponseData> Forbidden(HttpRequestData req, string message, CancellationToken ct) =>
        ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden, ApiResponse<object>.Fail(message), ct);
}
