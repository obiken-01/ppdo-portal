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
/// AIP endpoints under <c>/api/budget-planning/aip</c> (RAL-64).
/// Upload/Confirm require CanUploadAip. All others require CanAccessBudgetPlanning.
/// Upload accepts a raw binary body (Content-Type: application/octet-stream).
/// Confirm is stateless: the client echoes back the parsed hierarchy from the preview.
/// </summary>
public sealed class AipFunctions
{
    private readonly IAipService               _aip;
    private readonly IJwtMiddleware            _jwt;
    private readonly IPermissionService        _permissions;
    private readonly IRepository<FundingSource> _fsRepo;

    public AipFunctions(
        IAipService aip,
        IJwtMiddleware jwt,
        IPermissionService permissions,
        IRepository<FundingSource> fsRepo)
    {
        _aip         = aip;
        _jwt         = jwt;
        _permissions = permissions;
        _fsRepo      = fsRepo;
    }

    private Task<bool> CanAccess(User u)    => _permissions.CanAccessBudgetPlanningAsync(u);
    private Task<bool> CanUpload(User u)    => _permissions.CanUploadAipAsync(u);

    // ── GET /api/budget-planning/aip?fiscalYear=&status= ─────────────────────
    [Function("AipList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/aip")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        int? fiscalYear = int.TryParse(req.Query["fiscalYear"], out int fy) ? fy : null;
        IReadOnlyList<AipRecordDto> data = await _aip.GetAllAsync(fiscalYear, req.Query["status"], ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<AipRecordDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/aip/{id} ────────────────────────────────────
    [Function("AipGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/aip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _aip.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/aip/upload?fiscalYear= ─────────────────────
    // Body: raw XLSM bytes (Content-Type: application/octet-stream)
    [Function("AipUpload")]
    public async Task<HttpResponseData> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/upload")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanUpload, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["fiscalYear"], out int fiscalYear) || fiscalYear < 2000)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipImportPreviewDto>.Fail("Valid fiscalYear query parameter is required (e.g. ?fiscalYear=2027)."), ct);

        // Load active funding sources so the parser can flag unmatched codes.
        IReadOnlyList<FundingSource> fundingSources = await _fsRepo.GetAllAsync(ct);

        ServiceResult<AipImportPreviewDto> result =
            await _aip.ParsePreviewAsync(req.Body, fiscalYear, fundingSources, ct);

        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── POST /api/budget-planning/aip/confirm ────────────────────────────────
    [Function("AipConfirm")]
    public async Task<HttpResponseData> Confirm(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/confirm")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanUpload, ct);
        if (denied is not null) return denied;

        AipImportConfirmDto? body = await ConfigHttp.ReadBodyAsync<AipImportConfirmDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipRecordDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.ConfirmImportAsync(body, caller!.Id, ct), ct, HttpStatusCode.Created);
    }

    // ── DELETE /api/budget-planning/aip/{id}  (archive) ──────────────────────
    [Function("AipArchive")]
    public async Task<HttpResponseData> Archive(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/aip/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _aip.ArchiveAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/aip/{id}/finalize ───────────────────────────
    [Function("AipFinalize")]
    public async Task<HttpResponseData> Finalize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/{id:int}/finalize")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _aip.FinalizeAsync(id, ct), ct);
    }

    // ── POST /api/budget-planning/aip/{id}/unlock  (admin only) ──────────────
    [Function("AipUnlock")]
    public async Task<HttpResponseData> Unlock(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/{id:int}/unlock")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        if (caller!.Role is not (UserRole.SuperAdmin or UserRole.Admin))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.Forbidden,
                ApiResponse<AipRecordDto>.Fail("Admin or SuperAdmin role required to unlock records."), ct);

        return await ConfigHttp.FromResultAsync(req, await _aip.UnlockAsync(id, ct), ct);
    }
}
