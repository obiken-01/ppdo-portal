using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// WFP entry-screen "quick save" for procurement presets (RAL-119) — a lighter route onto the
/// same <see cref="IProcurementPresetService"/> used by <see cref="ConfigProcurementPresetFunctions"/>,
/// gated by CanAccessBudgetPlanning instead of CanManageConfig so anyone entering a WFP
/// procurement line item can save it as a reusable preset without config access. Presets are
/// shared across all offices/divisions regardless of which gate created them (§7.2).
///
/// The "Save as preset" trigger inside the WFP entry wizard itself is out of scope here (ticket
/// #11 / RAL-125) — this endpoint only exposes the API surface that trigger will call.
/// </summary>
public sealed class WfpProcurementPresetFunctions
{
    private readonly IProcurementPresetService _presets;
    private readonly IJwtMiddleware            _jwt;
    private readonly IPermissionService        _permissions;

    public WfpProcurementPresetFunctions(
        IProcurementPresetService presets, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _presets     = presets;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccessBudgetPlanning(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── POST /api/config/procurement-presets/quick-save ──
    [Function("ProcurementPresetsQuickSave")]
    public async Task<HttpResponseData> QuickSave(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/procurement-presets/quick-save")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null) return denied;

        UpsertProcurementPresetDto? body = await ConfigHttp.ReadBodyAsync<UpsertProcurementPresetDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ProcurementPresetDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _presets.CreateAsync(caller!, body, ct), ct, HttpStatusCode.Created);
    }
}
