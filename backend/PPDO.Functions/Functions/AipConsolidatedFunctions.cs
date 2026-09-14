using System.Collections.Specialized;
using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// The consolidated AIP (V18-55 / PPDO-73, <c>AIP_Review_Spec.md</c> §4 and §6.3a).
///
/// <para>
/// ⚠️ <b>Gated on <c>CanReviewAllOffices</c> — never on "is this caller in the host office".</b>
/// PPDO's own division users are host-office users and must not see the consolidated document
/// (tracker B4). The service checks the flag again on its own account.
/// </para>
/// </summary>
public sealed class AipConsolidatedFunctions
{
    private readonly IAipConsolidatedService _consolidated;
    private readonly IJwtMiddleware          _jwt;
    private readonly IPermissionService      _permissions;

    public AipConsolidatedFunctions(
        IAipConsolidatedService consolidated,
        IJwtMiddleware          jwt,
        IPermissionService      permissions)
    {
        _consolidated = consolidated;
        _jwt          = jwt;
        _permissions  = permissions;
    }

    private Task<bool> CanReviewAllOffices(User u) => _permissions.CanReviewAllOfficesAsync(u);

    // ── GET /api/budget-planning/aip/consolidated?fiscalYear=&sector= ─────────
    //
    // One sector sheet per call — a sheet at a time, as the workbook is laid out; GENERAL alone ran to
    // ~800 rows in FY2027.
    //
    // ⚠️ Route ordering: "consolidated" is not an int, so this cannot collide with
    // budget-planning/aip/{id:int} — the constraint is what keeps them apart.
    [Function("AipConsolidatedSheet")]
    public async Task<HttpResponseData> GetSheet(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/consolidated")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanReviewAllOffices, ct);
        if (denied is not null) return denied;

        NameValueCollection q = HttpUtility.ParseQueryString(req.Url.Query);

        if (!int.TryParse(q["fiscalYear"], out int fiscalYear) || fiscalYear <= 0)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipConsolidatedSheetDto>.Fail("fiscalYear is required."), ct);

        // The sector is validated in the service, so the refusal names the four it accepts.
        return await ConfigHttp.FromResultAsync(req,
            await _consolidated.GetSheetAsync(fiscalYear, q["sector"], caller!, ct), ct);
    }
}
