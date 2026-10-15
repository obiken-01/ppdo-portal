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
/// WFP Report preview endpoints under <c>/api/budget-planning/wfp/report</c> (RAL-132).
/// Read-only — gated by CanAccessWfp, same as the rest of the WFP surface (PPDO-198).
/// Preview and export are division-scoped through WfpDivisionScope (RAL-136, PPDO-198): finance and
/// Admin see every division; Staff see their own; Staff with no division see nothing.
///
/// <para>
/// ↩️ <b>PPDO-224: an entered year (<see cref="AipFiscalYears.IsEntered"/>) is the initial WFP, read off
/// the consolidated AIP.</b> The gate opens to <c>CanAccessBudgetPlanning</c> so an office can read its
/// own (spec decision 6), and the service applies the office scope, the Consolidated gate (409) and the
/// division rule. ⚠️ <b>FY ≤ 2027 is unchanged:</b> anyone without <c>CanAccessWfp</c> gets the same
/// bare 403 as before, and everyone else runs exactly the code that was here.
/// </para>
/// </summary>
public sealed class WfpReportFunctions
{
    private readonly IWfpReportService       _report;
    private readonly IWfpReportExcelService  _excel;
    private readonly IJwtValidator          _jwt;
    private readonly IPermissionService      _permissions;

    public WfpReportFunctions(
        IWfpReportService report, IWfpReportExcelService excel,
        IJwtValidator jwt, IPermissionService permissions)
    {
        _report      = report;
        _excel       = excel;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessWfpAsync(u);
    // PPDO-224 — the outer gate. CanAccessWfp implies it, so FY <= 2027 callers are unaffected.
    private Task<bool> CanAccessBudgetPlanning(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    /// <summary>The requested year when it is an entered one (the initial WFP), else null.</summary>
    private static int? EnteredYear(HttpRequestData req)
        => int.TryParse(req.Query["fiscalYear"], out int fy) && AipFiscalYears.IsEntered(fy) ? fy : null;

    /// <summary>
    /// The PPDO-224 front door: the caller and the entered year (null for FY 2027 and earlier), or the
    /// refusal. 401/403 as before, and for FY 2027 and earlier the same 403 a non-WFP reader always got.
    /// </summary>
    private async Task<(User? Caller, int? EnteredYear, HttpResponseData? Denied)> AuthorizeReportAsync(
        HttpRequestData req, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null || caller is null) return (null, null, denied);

        int? entered = EnteredYear(req);
        if (entered is null && !await CanAccess(caller))
            return (null, null, req.CreateResponse(HttpStatusCode.Forbidden));
        return (caller, entered, null);
    }
    private Task<DivisionScope> ScopeOf(User u) => WfpDivisionScope.ResolveAsync(u, _permissions);

    // ── GET /api/budget-planning/wfp/report/offices?fiscalYear= ──────────────
    // Office picker — only offices with at least a Draft WFP for the fiscal year.
    [Function("WfpReportOffices")]
    public async Task<HttpResponseData> GetEligibleOffices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/offices")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, int? enteredYear, HttpResponseData? denied) = await AuthorizeReportAsync(req, ct);
        if (denied is not null || caller is null) return denied!;

        if (enteredYear is int initialYear)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
                ApiResponse<IReadOnlyList<WfpReportOfficeDto>>.Ok(
                    await _report.GetInitialEligibleOfficesAsync(initialYear, caller, ct)), ct);

        if (!int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<WfpReportOfficeDto>>.Fail("fiscalYear query parameter is required."), ct);

        IReadOnlyList<WfpReportOfficeDto> data = await _report.GetEligibleOfficesAsync(fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<WfpReportOfficeDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/wfp/report/preview?officeId=&fiscalYear=&divisionId= ─────────
    // divisionId (RAL-136): division-scoped callers (not CanManagePpdoAllocation) are ALWAYS forced
    // to their own division — any divisionId they pass is ignored — so they can never read
    // another division's report by manipulating the query string. Finance officers may pass an
    // optional divisionId to narrow the otherwise-consolidated report to one division.
    [Function("WfpReportPreview")]
    public async Task<HttpResponseData> GetPreview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/preview")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, int? enteredYear, HttpResponseData? denied) = await AuthorizeReportAsync(req, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? requestedDivisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;

        // PPDO-224: the service owns scope, gate (409) and division for the initial WFP.
        if (enteredYear is not null)
            return await ConfigHttp.FromResultAsync(req,
                await _report.GetInitialReportAsync(officeId, fiscalYear, requestedDivisionId, caller, ct), ct);

        if (!WfpDivisionScope.TryFilter(await ScopeOf(caller), requestedDivisionId, out int? divisionId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                ApiResponse<WfpReportDto>.Fail("Report not found."), ct);

        ServiceResult<WfpReportDto> result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/wfp/report/export?officeId=&fiscalYear=&divisionId= ─────────
    // Same scoping rules as GetPreview. Returns the PBO-form-shaped .xlsx (RAL-159/v1.4.4).
    [Function("WfpReportExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/export")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, int? enteredYear, HttpResponseData? denied) = await AuthorizeReportAsync(req, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<WfpReportDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        int? requestedDivisionId = int.TryParse(req.Query["divisionId"], out int did) ? did : null;

        ServiceResult<WfpReportDto> result;
        if (enteredYear is not null)
        {
            // PPDO-224: the initial WFP keeps its own status codes (404 scope, 409 not Consolidated).
            result = await _report.GetInitialReportAsync(officeId, fiscalYear, requestedDivisionId, caller, ct);
            if (!result.IsSuccess) return await ConfigHttp.FromResultAsync(req, result, ct);
        }
        else
        {
            if (!WfpDivisionScope.TryFilter(await ScopeOf(caller), requestedDivisionId, out int? divisionId))
                return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                    ApiResponse<WfpReportDto>.Fail("Report not found."), ct);

            result = await _report.GetReportAsync(officeId, fiscalYear, divisionId, ct);
            if (!result.IsSuccess)
                return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.NotFound,
                    ApiResponse<WfpReportDto>.Fail(result.Error ?? "Report not found."), ct);
        }

        byte[] bytes = _excel.Export(result.Value!);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
        response.Headers.Add("Content-Disposition",
            $"attachment; filename=\"WFP_{result.Value!.OfficeCode}_{fiscalYear}.xlsx\"");
        await response.WriteBytesAsync(bytes);
        return response;
    }
}
