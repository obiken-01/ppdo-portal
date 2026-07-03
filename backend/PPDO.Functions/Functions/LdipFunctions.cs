using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// LDIP endpoints under <c>/api/budget-planning/ldip</c> (RAL-64, RAL-61).
/// All require CanAccessBudgetPlanning. The Unlock endpoint additionally requires Admin/SuperAdmin.
///
/// Office scoping (RAL-61): office users (users.office_id set) only see and mutate
/// their own office's records — the officeId is forced from the JWT user, never
/// trusted from the request. PPDO users see all and may filter with ?officeId=.
/// </summary>
public sealed class LdipFunctions
{
    private readonly ILdipService        _ldip;
    private readonly IJwtMiddleware      _jwt;
    private readonly IPermissionService  _permissions;
    private readonly IRepository<FundingSource> _fsRepo;

    public LdipFunctions(
        ILdipService ldip, IJwtMiddleware jwt, IPermissionService permissions, IRepository<FundingSource> fsRepo)
    {
        _ldip        = ldip;
        _jwt         = jwt;
        _permissions = permissions;
        _fsRepo      = fsRepo;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);
    private Task<bool> CanUpload(User u) => _permissions.CanUploadAipAsync(u);

    /// <summary>
    /// Returns a 403 response when an office-scoped caller targets a record that
    /// belongs to another office. Null when access is fine (or the record does not
    /// exist — the service will produce the NotFound).
    /// </summary>
    private async Task<HttpResponseData?> DenyForeignOfficeAsync(
        HttpRequestData req, User caller, int id, CancellationToken ct)
    {
        if (caller.OfficeId is null) return null;   // PPDO — full access

        ServiceResult<LdipRecordDetailDto> existing = await _ldip.GetByIdAsync(id, ct);
        if (existing.IsSuccess && existing.Value!.OfficeId != caller.OfficeId)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden,
                ApiResponse<LdipRecordDetailDto>.Fail("This LDIP record belongs to another office."), ct);
        return null;
    }

    // ── GET /api/budget-planning/ldip?status=&officeId= ──────────────────────
    [Function("LdipList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/ldip")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        // Office users are always scoped to their own office; PPDO may filter.
        int? officeId = caller!.OfficeId
            ?? (int.TryParse(req.Query["officeId"], out int parsed) ? parsed : null);

        IReadOnlyList<LdipRecordDto> data = await _ldip.GetAllAsync(req.Query["status"], officeId, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<LdipRecordDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/ldip/{id} ───────────────────────────────────
    [Function("LdipGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/ldip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        HttpResponseData? forbidden = await DenyForeignOfficeAsync(req, caller!, id, ct);
        if (forbidden is not null) return forbidden;

        return await ConfigHttp.FromResultAsync(req, await _ldip.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/ldip ───────────────────────────────────────
    [Function("LdipCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateLdipDto? body = await ConfigHttp.ReadBodyAsync<CreateLdipDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<LdipRecordDetailDto>.Fail("Request body is missing or malformed."), ct);

        // Office users always create for their own office, whatever the body says.
        if (caller!.OfficeId is not null)
            body = body with { OfficeId = caller.OfficeId };

        return await ConfigHttp.FromResultAsync(req,
            await _ldip.CreateAsync(body, caller.Id, ct), ct, HttpStatusCode.Created);
    }

    // ── PUT /api/budget-planning/ldip/{id} ───────────────────────────────────
    [Function("LdipUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budget-planning/ldip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        HttpResponseData? forbidden = await DenyForeignOfficeAsync(req, caller!, id, ct);
        if (forbidden is not null) return forbidden;

        UpdateLdipDto? body = await ConfigHttp.ReadBodyAsync<UpdateLdipDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<LdipRecordDetailDto>.Fail("Request body is missing or malformed."), ct);

        if (caller!.OfficeId is not null)
            body = body with { OfficeId = caller.OfficeId };

        return await ConfigHttp.FromResultAsync(req, await _ldip.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/budget-planning/ldip/{id}  (archive) ─────────────────────
    [Function("LdipArchive")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/ldip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        HttpResponseData? forbidden = await DenyForeignOfficeAsync(req, caller!, id, ct);
        if (forbidden is not null) return forbidden;

        return await ConfigHttp.FromResultAsync(req, await _ldip.ArchiveAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/ldip/{id}/finalize ─────────────────────────
    [Function("LdipFinalize")]
    public async Task<HttpResponseData> Finalize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip/{id:int}/finalize")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        HttpResponseData? forbidden = await DenyForeignOfficeAsync(req, caller!, id, ct);
        if (forbidden is not null) return forbidden;

        return await ConfigHttp.FromResultAsync(req, await _ldip.FinalizeAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/ldip/{id}/unlock  (admin only) ─────────────
    [Function("LdipUnlock")]
    public async Task<HttpResponseData> Unlock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip/{id:int}/unlock")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        if (caller!.Role is not (UserRole.SuperAdmin or UserRole.Admin))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden,
                ApiResponse<LdipRecordDto>.Fail("Admin or SuperAdmin role required to unlock records."), ct);

        return await ConfigHttp.FromResultAsync(req, await _ldip.UnlockAsync(id, ct), ct);
    }

    // ── File upload (RAL-113) ─────────────────────────────────────────────────
    // Upload/Confirm reuse CanUploadAip (PPDO-only) — same uploader role as AIP.
    // The workbook covers every office, so there is no officeId param — offices
    // are auto-detected by matching AIP ref codes against Config → Offices.

    // ── POST /api/budget-planning/ldip/upload?fiscalYearStart=&fiscalYearEnd= ──
    // Body: raw LDIP XLSX bytes (Content-Type: application/octet-stream)
    [Function("LdipUpload")]
    public async Task<HttpResponseData> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip/upload")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanUpload, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["fiscalYearStart"], out int fyStart) || fyStart < 2000)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<LdipImportPreviewDto>.Fail("Valid fiscalYearStart query parameter is required."), ct);
        if (!int.TryParse(req.Query["fiscalYearEnd"], out int fyEnd) || fyEnd < fyStart)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<LdipImportPreviewDto>.Fail("Valid fiscalYearEnd query parameter (>= fiscalYearStart) is required."), ct);

        IReadOnlyList<FundingSource> fundingSources = await _fsRepo.GetAllAsync(ct);

        // Buffer the request body into a MemoryStream before passing to ClosedXML —
        // Kestrel (Azure Functions isolated worker) disallows synchronous reads on the
        // HttpRequestStream; MemoryStream supports them so XLWorkbook.Load() succeeds.
        using MemoryStream xlsxBuffer = new();
        await req.Body.CopyToAsync(xlsxBuffer, ct);
        xlsxBuffer.Position = 0;

        ServiceResult<LdipImportPreviewDto> result =
            await _ldip.ParsePreviewAsync(xlsxBuffer, fyStart, fyEnd, fundingSources, ct);

        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── POST /api/budget-planning/ldip/confirm ───────────────────────────────
    [Function("LdipConfirm")]
    public async Task<HttpResponseData> Confirm(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/ldip/confirm")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanUpload, ct);
        if (denied is not null) return denied;

        LdipImportConfirmDto? body = await ConfigHttp.ReadBodyAsync<LdipImportConfirmDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<LdipRecordDto>>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _ldip.ConfirmImportAsync(body, caller!.Id, ct), ct, HttpStatusCode.Created);
    }
}
