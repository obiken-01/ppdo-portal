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

    // ── GET /api/budget-planning/aip/{id}/summary ─────────────────────────────
    [Function("AipGetSummary")]
    public async Task<HttpResponseData> GetSummary(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/aip/{id:int}/summary")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _aip.GetSummaryByIdAsync(id, ct), ct);
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

        // Buffer the request body into a MemoryStream before passing to ClosedXML.
        // Kestrel (Azure Functions isolated worker) disallows synchronous reads on the
        // HttpRequestStream; MemoryStream supports them so XLWorkbook.Load() succeeds.
        using MemoryStream xlsmBuffer = new();
        await req.Body.CopyToAsync(xlsmBuffer, ct);
        xlsmBuffer.Position = 0;

        ServiceResult<AipImportPreviewDto> result =
            await _aip.ParsePreviewAsync(xlsmBuffer, fiscalYear, fundingSources, ct);

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

    // ── Manual entry (RAL-62) — gated on CanAccessBudgetPlanning, NOT CanUploadAip ──
    // Office users who can never upload an .xlsm can still build an AIP by hand.

    // ── POST /api/budget-planning/aip  (create blank Manual AipRecord) ───────
    [Function("AipCreateManual")]
    public async Task<HttpResponseData> CreateManual(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateAipRecordDto? body = await ConfigHttp.ReadBodyAsync<CreateAipRecordDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipRecordDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.CreateManualRecordAsync(body, caller!.Id, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/budget-planning/aip/{aipId}/offices ─────────────────────────
    [Function("AipAddOffice")]
    public async Task<HttpResponseData> AddOffice(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/{aipId:int}/offices")] HttpRequestData req,
        int aipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateAipOfficeDto? body = await ConfigHttp.ReadBodyAsync<CreateAipOfficeDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipOfficeDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.AddOfficeAsync(aipId, body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/budget-planning/aip/offices/{officeId}/programs ────────────
    [Function("AipAddProgram")]
    public async Task<HttpResponseData> AddProgram(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/offices/{officeId:int}/programs")] HttpRequestData req,
        int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateAipProgramDto? body = await ConfigHttp.ReadBodyAsync<CreateAipProgramDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipProgramDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.AddProgramAsync(officeId, body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/budget-planning/aip/programs/{programId}/projects ──────────
    [Function("AipAddProject")]
    public async Task<HttpResponseData> AddProject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/programs/{programId:int}/projects")] HttpRequestData req,
        int programId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateAipProjectDto? body = await ConfigHttp.ReadBodyAsync<CreateAipProjectDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipProjectDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.AddProjectAsync(programId, body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/budget-planning/aip/projects/{projectId}/activities ────────
    [Function("AipAddActivity")]
    public async Task<HttpResponseData> AddActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/aip/projects/{projectId:int}/activities")] HttpRequestData req,
        int projectId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateAipActivityDto? body = await ConfigHttp.ReadBodyAsync<CreateAipActivityDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipActivityDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.AddActivityAsync(projectId, body, ct), ct, HttpStatusCode.Created);
    }

    // ── PUT /api/budget-planning/aip/{id}/activities/{activityId} ────────────
    // RAL-179 — inline per-activity edit. CanAccessBudgetPlanning, not CanUploadAip: editing an
    // already-imported/created record's fields is a correction, not a new import.
    [Function("AipUpdateActivity")]
    public async Task<HttpResponseData> UpdateActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budget-planning/aip/{id:int}/activities/{activityId:int}")] HttpRequestData req,
        int id, int activityId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        UpdateAipActivityDto? body = await ConfigHttp.ReadBodyAsync<UpdateAipActivityDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipActivityDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.UpdateActivityAsync(id, activityId, body, ct), ct);
    }

    // ── DELETE /api/budget-planning/aip/programs/{programId} ─────────────────
    // Mistakes happen (e.g. data entered under the wrong level) — Draft-only, cascades
    // to the program's projects/activities.
    [Function("AipDeleteProgram")]
    public async Task<HttpResponseData> DeleteProgram(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/aip/programs/{programId:int}")] HttpRequestData req,
        int programId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _aip.DeleteProgramAsync(programId, ct), ct);
    }

    // ── DELETE /api/budget-planning/aip/projects/{projectId} ─────────────────
    [Function("AipDeleteProject")]
    public async Task<HttpResponseData> DeleteProject(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/aip/projects/{projectId:int}")] HttpRequestData req,
        int projectId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _aip.DeleteProjectAsync(projectId, ct), ct);
    }

    // ── DELETE /api/budget-planning/aip/activities/{activityId} ──────────────
    [Function("AipDeleteActivity")]
    public async Task<HttpResponseData> DeleteActivity(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/aip/activities/{activityId:int}")] HttpRequestData req,
        int activityId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req, await _aip.DeleteActivityAsync(activityId, ct), ct);
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

    // ── PUT /api/budget-planning/aip/programs/{id:int}/function-band ─────────
    // v1.4 Q1: captured during WFP data entry (wfp/entry context picker), not AIP import.
    [Function("AipUpdateProgramFunctionBand")]
    public async Task<HttpResponseData> UpdateProgramFunctionBand(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budget-planning/aip/programs/{id:int}/function-band")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        UpdateAipProgramFunctionBandDto? body = await ConfigHttp.ReadBodyAsync<UpdateAipProgramFunctionBandDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipProgramDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.UpdateProgramFunctionBandAsync(id, body.FunctionBand, ct), ct);
    }

    // ── PUT /api/budget-planning/aip/activities/{id:int}/is-creation ─────────
    // v1.4 Q2: captured during WFP data entry (wfp/entry context picker), not AIP import.
    [Function("AipUpdateActivityIsCreation")]
    public async Task<HttpResponseData> UpdateActivityIsCreation(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budget-planning/aip/activities/{id:int}/is-creation")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        UpdateAipActivityIsCreationDto? body = await ConfigHttp.ReadBodyAsync<UpdateAipActivityIsCreationDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipActivityDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _aip.UpdateActivityIsCreationAsync(id, body.IsCreation, ct), ct);
    }
}
