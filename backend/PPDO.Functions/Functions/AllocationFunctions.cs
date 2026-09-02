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
/// Mutations split across two per-user grants (RAL-243): the ceiling upsert is gated on
/// CanManagePboCeiling (PBO finance officer — any office), while the division-allocation and
/// PPA-assignment upserts stay on CanManagePpdoAllocation (PPDO finance officer — splits PPDO's
/// own ceiling). Holding one does not grant the other; a user who set ceilings before v1.8.0
/// needs OverrideCanManagePboCeiling granted. All GET reads are gated on the broader CanAccessBudgetPlanning
/// so that regular WFP users — not just finance officers — can load the context the WFP
/// entry wizard needs (ceiling exists?, own division's allocation, assigned programs, setup
/// gate). GetDivisions additionally scopes non-finance callers to their own division's row —
/// other divisions' peso amounts stay finance-officer-only (v1.4.1, RAL-135-adjacent fix:
/// the entry wizard 403'd for every non-finance Staff user once the office/division
/// auto-select bug was fixed and they could actually reach this call).
///
/// <b>Office scoping (v1.8.0 — PPDO-18).</b> Every GET here takes a caller-supplied officeId and,
/// until this ticket, used it unchecked — so any Budget Planning user could read any other
/// office's ceilings, division split, PPA assignments and setup status by editing the query
/// string. Same class as the RAL-229 dashboard IDOR. All six are now clamped through
/// <see cref="ConfigHttp.ClampOfficeIdForCeiling"/>: a host-office caller and a CanManagePboCeiling
/// holder keep cross-office reads, everyone else is forced to their own office. The permission
/// gates below are deliberately unchanged — the fix is the office axis, not the grant.
///
/// The writes are scoped per grant, and the two grants are not the same:
///   ceiling PUT                       — cross-office by design; CanManagePboCeiling IS that authority.
///   division-allocation + PPA assign  — host-office only; CanManagePpdoAllocation is exclusive to
///                                       PPDO users, so a guest-office holder is a mis-grant and is
///                                       refused outright rather than allowed to write its own office.
/// Refused rather than clamped, unlike the reads: silently rewriting which office a peso amount
/// lands on is a worse failure than a 403.
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

    private Task<bool> CanManagePpdoAllocation(User u) => _permissions.CanManagePpdoAllocationAsync(u);
    private Task<bool> CanManagePboCeiling(User u)     => _permissions.CanManagePboCeilingAsync(u);
    private Task<bool> CanAccessBudgetPlanning(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    /// <summary>
    /// Clamps a caller-supplied officeId for the allocation-setup reads (PPDO-18). Call this
    /// AFTER the endpoint's int.TryParse validation so a malformed officeId is still a 400 rather
    /// than a silent fallback to the caller's own office.
    /// </summary>
    private async Task<int> ClampOfficeAsync(User caller, int requestedOfficeId, CancellationToken ct)
        => ConfigHttp.ClampOfficeIdForCeiling(
               caller, await _permissions.CanManagePboCeilingAsync(caller, ct), requestedOfficeId)
           ?? requestedOfficeId;

    // ── GET /api/budget-planning/allocation/ceiling?officeId=&fiscalYear=&fundingSourceId= ─────
    // Read is gated on CanAccessBudgetPlanning (not CanManagePpdoAllocation): every WFP
    // user — including non-finance office users — needs to know whether a ceiling
    // exists for the setup-complete gate. Mutations below stay finance-only.
    [Function("AllocationGetCeiling")]
    public async Task<HttpResponseData> GetCeiling(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/ceiling")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear) ||
            !int.TryParse(req.Query["fundingSourceId"], out int fundingSourceId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<BudgetCeilingDto>.Fail(
                    "officeId, fiscalYear, and fundingSourceId query parameters are required."), ct);

        officeId = await ClampOfficeAsync(caller, officeId, ct);

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
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<BudgetCeilingDto>>.Fail(
                    "officeId and fiscalYear query parameters are required."), ct);

        officeId = await ClampOfficeAsync(caller, officeId, ct);

        IReadOnlyList<BudgetCeilingDto> data = await _allocation.GetCeilingsAsync(officeId, fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<BudgetCeilingDto>>.Ok(data), ct);
    }

    // ── PUT /api/budget-planning/allocation/ceiling ───────────────────────────
    // Gated on CanManagePboCeiling, NOT CanManagePpdoAllocation (RAL-243). Setting a
    // ceiling is the Provincial Budget Office's authority and applies to any office;
    // the allocation grant only splits PPDO's own ceiling across its divisions. The two
    // are deliberately not OR-ed — see IPermissionService.CanManagePboCeilingAsync.
    //
    // Deliberately NOT office-clamped (PPDO-18). The gate IS the grant here: CanManagePboCeiling
    // means "may set a ceiling for any office", so clamping body.OfficeId to the caller's own
    // office would make RAL-243 unreachable and break the office picker in PPDO-17. Pinned by
    // AllocationFunctionsTests.UpsertCeiling_AsPboHolderInAGuestOffice_WritesTheRequestedForeignOffice
    // — do not add a clamp here "for consistency" with the two allocation writes below.
    [Function("AllocationUpsertCeiling")]
    public async Task<HttpResponseData> UpsertCeiling(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/allocation/ceiling")] HttpRequestData req,
        CancellationToken ct)
    {
        (_, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanManagePboCeiling, ct);
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
    // Gated on CanAccessBudgetPlanning (not CanManagePpdoAllocation): the WFP entry
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

        officeId = await ClampOfficeAsync(caller, officeId, ct);

        IReadOnlyList<DivisionAllocationDto> data =
            await _allocation.GetAllocationsAsync(officeId, fiscalYear, fundingSourceId, ct);

        if (!await CanManagePpdoAllocation(caller))
            data = data.Where(a => a.DivisionId == caller.DivisionId).ToList();

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<DivisionAllocationDto>>.Ok(data), ct);
    }

    // ── GET /api/budget-planning/allocation/divisions/all-funds?officeId=&fiscalYear= ─
    // RAL-166 follow-up: batches what was previously one GetDivisions call per active fund,
    // fired in parallel by the Allocation page, into a single request. Same read gate + the
    // same per-caller division clamp as GetDivisions above.
    [Function("AllocationGetDivisionsAllFunds")]
    public async Task<HttpResponseData> GetDivisionsAllFunds(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/divisions/all-funds")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<DivisionAllocationDto>>.Fail(
                    "officeId and fiscalYear query parameters are required."), ct);

        officeId = await ClampOfficeAsync(caller, officeId, ct);

        IReadOnlyList<DivisionAllocationDto> data =
            await _allocation.GetAllocationsForAllFundsAsync(officeId, fiscalYear, ct);

        if (!await CanManagePpdoAllocation(caller))
            data = data.Where(a => a.DivisionId == caller.DivisionId).ToList();

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<DivisionAllocationDto>>.Ok(data), ct);
    }

    // ── PUT /api/budget-planning/allocation/divisions ─────────────────────────
    // Host-office only (PPDO-18). CanManagePpdoAllocation is exclusive to PPDO users — Ralph
    // confirmed 2026-09-02 after a live account (pto.user, Provincial Treasurer's Office) was
    // found holding it by mistake. So a guest-office holder is a MIS-GRANT, and this refuses them
    // outright rather than letting them write their own office: the endpoint stops depending on
    // the grant being administered correctly. A host caller still writes any office, which is how
    // PPDO sets other offices up.
    //
    // The PBO ceiling grant does not reach here either — setting an office's ceiling is not
    // authority over how that office then splits it across its divisions.
    [Function("AllocationUpsertDivisions")]
    public async Task<HttpResponseData> UpsertDivisions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/allocation/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(
            req, _jwt, _permissions, CanManagePpdoAllocation, ct);
        if (denied is not null || caller is null) return denied!;

        if (!OfficeScope.Resolve(caller).SeeAll)
            return req.CreateResponse(HttpStatusCode.Forbidden);

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
    // Gated on CanAccessBudgetPlanning (not CanManagePpdoAllocation): the WFP entry
    // wizard needs this to know which programs are assigned to the current
    // division. No monetary data here (just a PPA → division-id mapping), unlike
    // GetDivisions above, so no further per-caller filtering is needed.
    [Function("AllocationGetPrograms")]
    public async Task<HttpResponseData> GetPrograms(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/allocation/programs")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId) ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<ProgramAssignmentDto>>.Fail(
                    "officeId and fiscalYear query parameters are required."), ct);

        officeId = await ClampOfficeAsync(caller, officeId, ct);

        IReadOnlyList<ProgramAssignmentDto> data =
            await _allocation.GetProgramAssignmentsAsync(officeId, fiscalYear, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<ProgramAssignmentDto>>.Ok(data), ct);
    }

    // ── PUT /api/budget-planning/allocation/programs ──────────────────────────
    // Host-office only (PPDO-18). The payload carries no office id — the office is resolved from
    // OfficeRefCode inside the service — so there is nothing to clamp or compare at this layer.
    // The only office authority assertable here is host-office, which is exactly what
    // CanManagePpdoAllocation is. Same idiom as GetBudgetPlanningDashboard's RAL-230 guard.
    [Function("AllocationUpsertProgram")]
    public async Task<HttpResponseData> UpsertProgram(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/allocation/programs")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeWriteAsync(
            req, _jwt, _permissions, CanManagePpdoAllocation, ct);
        if (denied is not null || caller is null) return denied!;

        if (!OfficeScope.Resolve(caller).SeeAll)
            return req.CreateResponse(HttpStatusCode.Forbidden);

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
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null || caller is null) return denied!;

        if (!int.TryParse(req.Query["officeId"], out int officeId)   ||
            !int.TryParse(req.Query["fiscalYear"], out int fiscalYear) ||
            !int.TryParse(req.Query["divisionId"], out int divisionId))
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AllocationSetupStatusDto>.Fail(
                    "officeId, fiscalYear, and divisionId query parameters are required."), ct);

        officeId = await ClampOfficeAsync(caller, officeId, ct);

        AllocationSetupStatusDto status =
            await _allocation.GetSetupStatusAsync(officeId, fiscalYear, divisionId, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<AllocationSetupStatusDto>.Ok(status), ct);
    }
}
