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

    /// <summary>
    /// ↩️ **Either reviewer flag admits** since PPDO-90 — a department head reads their OWN office's
    /// Annex B here. The handler no longer decides *which* offices: the service resolves the scope,
    /// because getting it wrong (a host-office department head resolving to every office) is a data
    /// leak, and that logic belongs where it can be unit-tested rather than in an HTTP handler.
    /// </summary>
    private async Task<bool> CanOpenReport(User u)
        => await _permissions.CanReviewAllOfficesAsync(u)
        || await _permissions.CanReviewBudgetPlanningAsync(u);

    /// <summary>`officeId` is optional and, for a department head, ignored in favour of their own.</summary>
    private static int? OfficeIdOf(NameValueCollection q)
        => int.TryParse(q["officeId"], out int id) && id > 0 ? id : null;

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
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanOpenReport, ct);
        if (denied is not null) return denied;

        NameValueCollection q = HttpUtility.ParseQueryString(req.Url.Query);

        if (!int.TryParse(q["fiscalYear"], out int fiscalYear) || fiscalYear <= 0)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipConsolidatedSheetDto>.Fail("fiscalYear is required."), ct);

        // The sector is validated in the service, so the refusal names the four it accepts.
        return await ConfigHttp.FromResultAsync(req,
            await _consolidated.GetSheetAsync(fiscalYear, q["sector"], OfficeIdOf(q), caller!, ct), ct);
    }

    // ── GET /api/budget-planning/aip/consolidated/export?fiscalYear= ──────────
    //
    // The whole fiscal year as the province's Annex B workbook — all four sector sheets (V18-60 /
    // PPDO-84, AIP_Form_Spec.md §12). FY≤2027 (400) and an unopened year (404) are the service's.
    //
    // ⚠️ Route ordering: "consolidated/export" is two literal segments; every sibling under
    // budget-planning/aip/ with a parameter constrains it to :int, so nothing else can match.
    [Function("AipConsolidatedExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/consolidated/export")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanOpenReport, ct);
        if (denied is not null) return denied;

        NameValueCollection q = HttpUtility.ParseQueryString(req.Url.Query);

        if (!int.TryParse(q["fiscalYear"], out int fiscalYear) || fiscalYear <= 0)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipFormExportFileDto>.Fail("fiscalYear is required."), ct);

        ServiceResult<AipFormExportFileDto> result =
            await _consolidated.ExportWorkbookAsync(fiscalYear, OfficeIdOf(q), caller!, ct);

        // A refusal is an envelope the page can read, never a file.
        if (!result.IsSuccess)
            return await ConfigHttp.FromResultAsync(req, result, ct);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            $"attachment; filename=\"{result.Value!.FileName}\"");
        await response.WriteBytesAsync(result.Value.Content, ct);
        return response;
    }
}
