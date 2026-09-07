using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// The submit checklist, the submit action and the ceiling read (V18-42 / PPDO-52,
/// V18-49 / PPDO-59, V18-46 / PPDO-56 — spec §4).
///
/// <para>
/// ⚠️ <b>Readiness is a GET and submit is a POST, and they run the same checks.</b> The GET exists
/// so the office can fix things without guessing; it is not a lighter version of the gate, and the
/// POST does not trust it — between the two calls PBO can cut a ceiling and a colleague can delete
/// a line.
/// </para>
///
/// <para>
/// All three are gated on <c>CanAccessBudgetPlanning</c>. Which office is acted on comes from the
/// <b>caller</b>, never from the route or the body: submit is an act on your own work.
/// </para>
/// </summary>
public sealed class AipSubmitFunctions
{
    private readonly IAipSubmitService  _submit;
    private readonly IAipCeilingService _ceiling;
    private readonly IAipRepository     _aipRepo;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public AipSubmitFunctions(
        IAipSubmitService  submit,
        IAipCeilingService ceiling,
        IAipRepository     aipRepo,
        IJwtMiddleware     jwt,
        IPermissionService permissions)
    {
        _submit      = submit;
        _ceiling     = ceiling;
        _aipRepo     = aipRepo;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/aip/{aipId}/readiness ───────────────────────
    [Function("AipReadiness")]
    public async Task<HttpResponseData> Readiness(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/readiness")] HttpRequestData req,
        int aipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _submit.GetReadinessAsync(aipId, caller!, ct), ct);
    }

    // ── POST /api/budget-planning/aip/{aipId}/submit ─────────────────────────
    [Function("AipSubmit")]
    public async Task<HttpResponseData> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/submit")] HttpRequestData req,
        int aipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        // No body: what is submitted is decided by who is asking, not by what they send.
        return await ConfigHttp.FromResultAsync(req,
            await _submit.SubmitAsync(aipId, caller!, ct), ct);
    }

    // ── GET /api/budget-planning/aip/{aipId}/ceiling ─────────────────────────
    // ⚠️ Remaining may be NEGATIVE and must render as such — it is the only signal an office gets
    // that PBO cut its ceiling below what is already encoded (A5-b).
    [Function("AipCeiling")]
    public async Task<HttpResponseData> Ceiling(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/ceiling")] HttpRequestData req,
        int aipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        // Resolve the caller's own group row in this record — the ceiling is an office-level figure
        // and the caller may only ask about their own office (OfficeScope, DECISION F).
        IReadOnlyList<AipOffice> offices = await _aipRepo.GetOfficesByAipIdAsync(aipId, ct);
        OfficeScope scope = OfficeScope.Resolve(caller!);
        AipOffice? mine = offices.FirstOrDefault(o =>
            scope.Permits(o.OfficeId)
            && (caller!.OfficeId is not int id || o.OfficeId == id));

        if (mine is null)
            return await ConfigHttp.FromResultAsync(req,
                ServiceResult<AipCeilingStatusDto>.NotFound(
                    $"No AIP office found for you in record {aipId}."), ct);

        return await ConfigHttp.FromResultAsync(req,
            ServiceResult<AipCeilingStatusDto>.Ok(await _ceiling.GetStatusAsync(mine.Id, ct)), ct);
    }
}
