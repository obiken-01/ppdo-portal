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
/// Allocation endpoints under <c>/api/budget-planning/allocation</c> (RAL-99).
///
/// Ceiling + division allocation + PPA→division assignment are all gated on
/// CanManageAllocation (finance officer only). The setup-status check is gated on
/// CanAccessBudgetPlanning so that regular WFP users can query the gate before entry.
///
/// Amounts are in PESOS — no ×1000 conversion here (that lives in the WFP page layer).
/// </summary>
public sealed class AllocationFunctions
{
    private readonly IAllocationService _allocation;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public AllocationFunctions(
        IAllocationService  allocation,
        IJwtMiddleware      jwt,
        IPermissionService  permissions)
    {
        _allocation  = allocation;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageAllocation(User u) => _permissions.CanManageAllocationAsync(u);
    private Task<bool> CanAccessBudgetPlanning(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/allocation/ceiling?officeId=&fiscalYear= ─────
    [Function("AllocationGetCeiling")]
    public async Task<HttpResponseData> GetCeiling(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/ceiling")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageAllocation, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<BudgetCeilingDto>.Fail("officeId and fiscalYear query parameters are required."), ct);

        ServiceResult<BudgetCeilingDto> result = await _allocation.GetCeilingAsync(officeId, fiscalYear, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── PUT /api/budget-planning/allocation/ceiling ───────────────────────────
    [Function("AllocationUpsertCeiling")]
    public async Task<HttpResponseData> UpsertCeiling(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/allocation/ceiling")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageAllocation, ct);
        if (denied is not null) return denied;

        UpsertCeilingDto? body = await ConfigHttp.ReadBodyAsync<UpsertCeilingDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<BudgetCeilingDto>.Fail("Request body is missing or malformed."), ct);

        ServiceResult<BudgetCeilingDto> result =
            await _allocation.UpsertCeilingAsync(body.OfficeId, body.FiscalYear, body.Amount, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/allocation/divisions?officeId=&fiscalYear= ───
    [Function("AllocationGetDivisions")]
    public async Task<HttpResponseData> GetDivisions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageAllocation, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<DivisionAllocationDto>>.Fail(
                    "officeId and fiscalYear query parameters are required."), ct);

        IReadOnlyList<DivisionAllocationDto> data =
            await _allocation.GetAllocationsAsync(officeId, fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<DivisionAllocationDto>>.Ok(data), ct);
    }

    // ── PUT /api/budget-planning/allocation/divisions ─────────────────────────
    [Function("AllocationUpsertDivisions")]
    public async Task<HttpResponseData> UpsertDivisions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/allocation/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageAllocation, ct);
        if (denied is not null) return denied;

        UpsertAllocationsDto? body = await ConfigHttp.ReadBodyAsync<UpsertAllocationsDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<DivisionAllocationDto>>.Fail(
                    "Request body is missing or malformed."), ct);

        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result =
            await _allocation.UpsertAllocationsAsync(body.OfficeId, body.FiscalYear, body.Allocations, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/allocation/programs?officeId=&fiscalYear= ────
    [Function("AllocationGetPrograms")]
    public async Task<HttpResponseData> GetPrograms(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/programs")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageAllocation, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<ProgramAssignmentDto>>.Fail(
                    "officeId and fiscalYear query parameters are required."), ct);

        IReadOnlyList<ProgramAssignmentDto> data =
            await _allocation.GetProgramAssignmentsAsync(officeId, fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<ProgramAssignmentDto>>.Ok(data), ct);
    }

    // ── PUT /api/budget-planning/allocation/programs ──────────────────────────
    [Function("AllocationUpsertProgram")]
    public async Task<HttpResponseData> UpsertProgram(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/allocation/programs")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageAllocation, ct);
        if (denied is not null) return denied;

        UpsertProgramAssignmentDto? body =
            await ConfigHttp.ReadBodyAsync<UpsertProgramAssignmentDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ProgramAssignmentDto>.Fail("Request body is missing or malformed."), ct);

        ServiceResult<ProgramAssignmentDto> result =
            await _allocation.UpsertProgramAssignmentAsync(body, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/allocation/status?officeId=&fiscalYear=&divisionId= ─
    // Gated on CanAccessBudgetPlanning — called by regular WFP users for the gate check.
    [Function("AllocationGetStatus")]
    public async Task<HttpResponseData> GetStatus(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/status")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId)   ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear) ||
            !int.TryParse(req.Query["divisionId"], out int divisionId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AllocationSetupStatusDto>.Fail(
                    "officeId, fiscalYear, and divisionId query parameters are required."), ct);

        AllocationSetupStatusDto status =
            await _allocation.GetSetupStatusAsync(officeId, fiscalYear, divisionId, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<AllocationSetupStatusDto>.Ok(status), ct);
    }
}
