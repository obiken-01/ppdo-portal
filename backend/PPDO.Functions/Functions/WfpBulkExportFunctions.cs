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
/// The bulk initial WFP export under <c>/api/budget-planning/wfp/report/bulk-exports</c> (v1.9.0 —
/// PPDO-228, <c>docs/v1.9/Bulk_Initial_WFP_Export_Spec.md</c> §4). Thin: JWT, the PPDO report-reader
/// gate, then <see cref="IInitialWfpExportService"/>, which owns every rule and checks the gate again.
///
/// <para>
/// These only queue and read jobs. The ZIP is built later by the worker, run every 30 s by
/// <see cref="ExportJobTimerFunctions"/>.
/// </para>
/// </summary>
public sealed class WfpBulkExportFunctions
{
    private readonly IInitialWfpExportService _exports;
    private readonly IJwtValidator            _jwt;
    private readonly IPermissionService       _permissions;

    public WfpBulkExportFunctions(IInitialWfpExportService exports, IJwtValidator jwt, IPermissionService permissions)
    {
        _exports     = exports;
        _jwt         = jwt;
        _permissions = permissions;
    }

    /// <summary>Decision 4: PPDO report readers, the <c>CanAccessWfp</c> holders who read every office.</summary>
    private Task<bool> IsPpdoReportReader(User u) => _permissions.CanAccessWfpAsync(u);

    // ── POST /api/budget-planning/wfp/report/bulk-exports  { fiscalYear } ──────────────────────
    // 202 a new job; 200 the year's job that is already Queued or Running; 400 FY <= 2027; 409 no
    // office Consolidated yet.
    [Function("WfpBulkExportRequest")]
    public async Task<HttpResponseData> Request(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/wfp/report/bulk-exports")] HttpRequestData req,
        CancellationToken ct)
    {
        // ⚠️ AuthorizeAsync, not AuthorizeWriteAsync: this queues a download of reports the caller may
        // already read one by one. It changes no plan content, so the reviewer write-denial does not
        // apply, and this class is left out of ReviewerWriteGuardCoverageTests like WfpReportFunctions.
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, IsPpdoReportReader, ct);
        if (denied is not null || caller is null) return denied!;

        RequestInitialWfpExportDto? body = await ConfigHttp.ReadBodyAsync<RequestInitialWfpExportDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<InitialWfpExportJobDto>.Fail("Request body is missing or malformed."), ct);

        ServiceResult<InitialWfpExportRequestResult> result = await _exports.RequestAsync(body.FiscalYear, caller, ct);
        if (!result.IsSuccess)
            return await ConfigHttp.FromResultAsync(req, ServiceResult<InitialWfpExportJobDto>.FromError(result), ct);

        return await ConfigHttp.FromResultAsync(req,
            ServiceResult<InitialWfpExportJobDto>.Ok(result.Value!.Job), ct,
            result.Value.Created ? HttpStatusCode.Accepted : HttpStatusCode.OK);
    }

    // ── GET /api/budget-planning/wfp/report/bulk-exports?fiscalYear= ───────────────────────────
    // The year's latest job, which the Report page restores on load; 204 when the year has none.
    [Function("WfpBulkExportLatest")]
    public async Task<HttpResponseData> GetLatest(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/bulk-exports")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, IsPpdoReportReader, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<InitialWfpExportJobDto>.Fail("fiscalYear query parameter is required."), ct);

        ServiceResult<InitialWfpExportJobDto?> result = await _exports.GetLatestAsync(fiscalYear, caller, ct);
        if (result.IsSuccess && result.Value is null)
            return req.CreateResponse(HttpStatusCode.NoContent);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/wfp/report/bulk-exports/{id} ──────────────────────────────────
    // One job, for the page's 3-second poll while it is Queued or Running.
    [Function("WfpBulkExportGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/bulk-exports/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, IsPpdoReportReader, ct);
        if (denied is not null || caller is null) return denied!;

        return await ConfigHttp.FromResultAsync(req, await _exports.GetAsync(id, caller, ct), ct);
    }

    // ── GET /api/budget-planning/wfp/report/bulk-exports/{id}/download ─────────────────────────
    // The ZIP, streamed through our API from the file store; 409 while not finished, 410 once expired.
    [Function("WfpBulkExportDownload")]
    public async Task<HttpResponseData> Download(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/wfp/report/bulk-exports/{id:int}/download")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, IsPpdoReportReader, ct);
        if (denied is not null || caller is null) return denied!;

        ServiceResult<InitialWfpExportDownload> result = await _exports.OpenDownloadAsync(id, caller, ct);
        if (!result.IsSuccess)
            return await ConfigHttp.FromResultAsync(req, result, ct);

        await using Stream content = result.Value!.Content;

        // ⚠️ Headers before the first body write, or the real host drops them (PPDO-183).
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/zip");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"{result.Value.FileName}\"");
        await content.CopyToAsync(response.Body, ct);
        return response;
    }
}
