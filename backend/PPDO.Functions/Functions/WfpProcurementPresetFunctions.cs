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
/// WFP entry-screen surface for procurement presets (RAL-119 quick-save; RAL-125 list) — lighter
/// routes onto the same <see cref="IProcurementPresetService"/> used by
/// <see cref="ConfigProcurementPresetFunctions"/>, gated by CanAccessBudgetPlanning instead of
/// CanManageConfig so anyone entering a WFP procurement line item can browse and save presets
/// without config access. Presets are shared across all offices/divisions regardless of which
/// gate created them (§7.2).
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

    // ── GET /api/config/procurement-presets/for-entry?accountId=&active= ──
    // A distinct route from ConfigProcurementPresetFunctions.List (same method+base path can't
    // share a route) so the WFP entry wizard's "Load preset" can list presets for its current
    // account without CanManageConfig — accountId is required here (entry always has an account
    // in context; the "all accounts" view is the config page's concern, not the wizard's).
    [Function("ProcurementPresetsListForEntry")]
    public async Task<HttpResponseData> ListForEntry(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/procurement-presets/for-entry")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["accountId"], out int accountId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<ProcurementPresetDto>>.Fail("accountId query parameter is required."), ct);

        IReadOnlyList<ProcurementPresetDto> data = await _presets.GetByAccountAsync(
            accountId, ActiveFilterParser.Parse(req.Query["active"]), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<ProcurementPresetDto>>.Ok(data), ct);
    }

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
