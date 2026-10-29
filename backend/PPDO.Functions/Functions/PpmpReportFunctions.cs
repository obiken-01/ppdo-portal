using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// PPMP Report preview endpoint under <c>/api/budget-planning/ppmp/report</c> (v1.5 — RAL-183).
/// Read-only. The office picker is shared with the WFP report
/// (<c>/api/budget-planning/wfp/report/offices</c>) — "offices with a WFP" is identical for both — so
/// this class adds only the preview and export routes.
///
/// <para>
/// ↩️ <b>PPDO-265 (v1.9.2): PPDO only, scoped like the WFP report.</b> The report is built from PPDO's
/// WFP procurement items, so it takes the WFP report's FY ≤ 2027 rules unchanged: the gate is
/// <c>CanAccessWfp</c> (host office; guest offices hold Budget Planning by default since v1.8, which
/// used to be the only check here), and the division is <see cref="WfpDivisionScope"/> (RAL-136,
/// PPDO-198): finance and Admin see every division, Staff their own, Staff with no division nothing.
/// </para>
///
/// <para>
/// ↩️ <b>PPDO-263 (v1.9.2): an entered year (<see cref="AipFiscalYears.IsEntered"/>) is the initial PPMP,
/// read off the consolidated AIP's procurement items.</b> Unlike the initial WFP, the gate stays
/// <c>CanAccessWfp</c>: a guest office's department head gets 403, even for their own office (spec
/// decision 8). The service owns the Consolidated gate (409) and the division rule.
/// </para>
/// </summary>
public sealed class PpmpReportFunctions
{
    private readonly IPpmpReportService       _report;
    private readonly IPpmpReportExcelService  _excel;
    private readonly IJwtValidator           _jwt;
    private readonly IPermissionService       _permissions;
    private readonly bool                    _enabled;

    // ppmpEnabled defaults to the feature flag; DI leaves it at the default (bool is not registered).
    // Tests pass false to keep the switched-off path covered (PPDO-236).
    public PpmpReportFunctions(
        IPpmpReportService report, IPpmpReportExcelService excel,
        IJwtValidator jwt, IPermissionService permissions, bool? ppmpEnabled = null)
    {
        _enabled     = ppmpEnabled ?? FeatureFlags.PpmpReportEnabled;
        _report      = report;
        _excel       = excel;
        _jwt         = jwt;
        _permissions = permissions;
    }

    // PPDO-265: CanAccessWfp, not CanAccessBudgetPlanning — a guest office has the latter by default.
    private Task<bool> CanAccess(User u) => _permissions.CanAccessWfpAsync(u);
    private Task<DivisionScope> ScopeOf(User u) => WfpDivisionScope.ResolveAsync(u, _permissions);

    /// <summary>PPDO-263: the initial PPMP is served for an entered year.</summary>
    private static bool IsEnteredYear(int fiscalYear) => AipFiscalYears.IsEntered(fiscalYear);

    // ── GET /api/budget-planning/ppmp/report/preview?officeId=&fiscalYear=&divisionId= ─────────
    // divisionId (RAL-136): division-scoped callers are ALWAYS forced to their own division — any
    // divisionId they pass is ignored — so they can never read another division's report by
    // manipulating the query string. Finance and Admin may pass an optional divisionId to narrow the
    // otherwise-consolidated (office-level) report to one division.
    [Function("PpmpReportPreview")]
    public async Task<HttpResponseData> GetPreview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/ppmp/report/preview")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!_enabled) return req.CreateResponse(HttpStatusCode.NotFound);

        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<PpmpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? requestedDivisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;

        // PPDO-263: the service owns the gate (409) and the division for the initial PPMP.
        if (IsEnteredYear(fiscalYear))
            return await ConfigHttp.FromResultAsync(req,
                await _report.GetInitialReportAsync(officeId, fiscalYear, requestedDivisionId, caller, ct), ct);

        if (!WfpDivisionScope.TryFilter(await ScopeOf(caller), requestedDivisionId, out int? divisionId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                ApiResponse<PpmpReportDto>.Fail("Report not found."), ct);

        ServiceResult<PpmpReportDto> result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/ppmp/report/export?officeId=&fiscalYear=&divisionId= ─────────
    // Same scoping rules as GetPreview. Returns the province-PPMP-form-shaped .xlsx (RAL-184).
    [Function("PpmpReportExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/ppmp/report/export")] HttpRequestData req,
        CancellationToken ct)
    {
        if (!_enabled) return req.CreateResponse(HttpStatusCode.NotFound);

        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<PpmpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? requestedDivisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;

        ServiceResult<PpmpReportDto> result;
        if (IsEnteredYear(fiscalYear))
        {
            // PPDO-263: the initial PPMP keeps its own status codes (404 missing, 409 not Consolidated).
            result = await _report.GetInitialReportAsync(officeId, fiscalYear, requestedDivisionId, caller, ct);
            if (!result.IsSuccess) return await ConfigHttp.FromResultAsync(req, result, ct);
        }
        else
        {
            if (!WfpDivisionScope.TryFilter(await ScopeOf(caller), requestedDivisionId, out int? divisionId))
                return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                    ApiResponse<PpmpReportDto>.Fail("Report not found."), ct);

            result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
            if (!result.IsSuccess)
                return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                    ApiResponse<PpmpReportDto>.Fail(result.Error ?? "Report not found."), ct);
        }

        byte[] bytes = _excel.Export(result.Value!);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            $"attachment; filename=\"PPMP_{result.Value!.OfficeCode}_{fiscalYear}.xlsx\"");
        await response.WriteBytesAsync(bytes);
        return response;
    }
}
