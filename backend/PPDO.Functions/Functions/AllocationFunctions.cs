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
/// Mutations (ceiling/allocation/assignment upserts) stay gated on CanManageAllocation
/// (finance officer only). All GET reads are gated on the broader CanAccessBudgetPlanning
/// so that regular WFP users — not just finance officers — can load the context the WFP
/// entry wizard needs (ceiling exists?, own division's allocation, assigned programs, setup
/// gate). GetDivisions additionally scopes non-finance callers to their own division's row —
/// other divisions' peso amounts stay finance-officer-only (v1.4.1, RAL-135-adjacent fix:
/// the entry wizard 403'd for every non-finance Staff user once the office/division
/// auto-select bug was fixed and they could actually reach this call).
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

    // ── GET /api/budget-planning/allocation/ceiling?officeId=&fiscalYear=&fundingSourceId= ─────
    // Read is gated on CanAccessBudgetPlanning (not CanManageAllocation): every WFP
    // user — including non-finance office users — needs to know whether a ceiling
    // exists for the setup-complete gate. Mutations below stay finance-only.
    [Function("AllocationGetCeiling")]
    public async Task<HttpResponseData> GetCeiling(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/ceiling")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear) ||
            !int.TryParse(req.Query["fundingSourceId"], out int fundingSourceId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<BudgetCeilingDto>.Fail(
                    "officeId, fiscalYear, and fundingSourceId query parameters are required."), ct);

        ServiceResult<BudgetCeilingDto> result =
            await _allocation.GetCeilingAsync(officeId, fiscalYear, fundingSourceId, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/allocation/ceilings?officeId=&fiscalYear= ────
    // Every fund source's ceiling for the office+FY in one call (v1.4.3 — RAL-154), for the
    // Allocation page's per-fund-source sections. Same read gate as GetCeiling above.
    [Function("AllocationGetCeilings")]
    public async Task<HttpResponseData> GetCeilings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/ceilings")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null) return denied;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<BudgetCeilingDto>>.Fail(
                    "officeId and fiscalYear query parameters are required."), ct);

        IReadOnlyList<BudgetCeilingDto> data = await _allocation.GetCeilingsAsync(officeId, fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<BudgetCeilingDto>>.Ok(data), ct);
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

        ServiceResult<BudgetCeilingDto> result = await _allocation.UpsertCeilingAsync(
            body.OfficeId, body.FiscalYear, body.FundingSourceId, body.Amount, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/allocation/divisions?officeId=&fiscalYear=&fundingSourceId= ───
    // Gated on CanAccessBudgetPlanning (not CanManageAllocation): the WFP entry
    // wizard needs a regular division-scoped user's own allocation amount to show
    // the budget banner. Non-finance callers only ever see their own division's
    // row — other divisions' peso amounts are finance-officer-only.
    [Function("AllocationGetDivisions")]
    public async Task<HttpResponseData> GetDivisions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear) ||
            !int.TryParse(req.Query["fundingSourceId"], out int fundingSourceId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<DivisionAllocationDto>>.Fail(
                    "officeId, fiscalYear, and fundingSourceId query parameters are required."), ct);

        IReadOnlyList<DivisionAllocationDto> data =
            await _allocation.GetAllocationsAsync(officeId, fiscalYear, fundingSourceId, ct);

        if (!await CanManageAllocation(caller))
            data = data.Where(a => a.DivisionId == caller.DivisionId).ToList();

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

        ServiceResult<IReadOnlyList<DivisionAllocationDto>> result = await _allocation.UpsertAllocationsAsync(
            body.OfficeId, body.FiscalYear, body.FundingSourceId, body.Allocations, ct);
        return await ConfigHttp.FromResultAsync(req, result, ct);
    }

    // ── GET /api/budget-planning/allocation/programs?officeId=&fiscalYear= ────
    // Gated on CanAccessBudgetPlanning (not CanManageAllocation): the WFP entry
    // wizard needs this to know which programs are assigned to the current
    // division. No monetary data here (just a PPA → division-id mapping), unlike
    // GetDivisions above, so no further per-caller filtering is needed.
    [Function("AllocationGetPrograms")]
    public async Task<HttpResponseData> GetPrograms(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/programs")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
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
