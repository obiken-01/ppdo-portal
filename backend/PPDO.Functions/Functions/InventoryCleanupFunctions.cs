// ⚠️ Dev/maintenance-only endpoint (wipes ALL inventory records).
// Supports the v1.2 clean-slate migration (RAL-97). Remove or keep behind DevCleanupKey
// for production safety — it returns 403 unless DevCleanupKey is configured.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Configuration;
using PPDO.Application.Common;
using PPDO.Application.Services;

namespace PPDO.Functions.Functions;

/// <summary>
/// Dev-only cleanup endpoint for wiping all inventory records (PurchaseRequests, PRItems,
/// Deliveries, DeliveryItems, Distributions, ItemMasters). Needed so the v1.2 clean-slate
/// migration can be applied (PurchaseRequests/Distributions gain a NOT NULL division_id FK).
///
/// Trigger via Postman; no UI. Two safeguards:
///   1. X-Dev-Cleanup-Key header matching the DevCleanupKey config value.
///   2. If DevCleanupKey is absent/empty in config, returns 403 immediately (safe-fail —
///      it is never set in Azure production App Settings).
/// </summary>
public sealed class InventoryCleanupFunctions
{
    private readonly IInventoryCleanupService _cleanup;
    private readonly IConfiguration           _config;

    public InventoryCleanupFunctions(IInventoryCleanupService cleanup, IConfiguration config)
    {
        _cleanup = cleanup;
        _config  = config;
    }

    // ── DELETE /api/inventory/cleanup ─────────────────────────────────────────
    [Function("InventoryCleanup")]
    public async Task<HttpResponseData> Cleanup(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "inventory/cleanup")] HttpRequestData req,
        CancellationToken ct)
    {
        string? devKey = _config["DevCleanupKey"];
        if (string.IsNullOrWhiteSpace(devKey))
            return await Forbidden(req, "Cleanup endpoint is not available in this environment.", ct);

        string? headerKey = req.Headers.TryGetValues("X-Dev-Cleanup-Key", out IEnumerable<string>? vals)
            ? vals.FirstOrDefault()
            : null;

        if (headerKey != devKey)
            return await Forbidden(req, "Invalid or missing X-Dev-Cleanup-Key header.", ct);

        InventoryPurgeResult result = await _cleanup.PurgeAllAsync(ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<object>.Ok(result, "Inventory records purged."), ct);
    }

    private static Task<HttpResponseData> Forbidden(HttpRequestData req, string message, CancellationToken ct) =>
        ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden, ApiResponse<object>.Fail(message), ct);
}
