using System.Collections.Specialized;
using System.Net;
using System.Web;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// The PGOM Investment Proposal endpoints (v1.8.0 Demo 2.15 — PPDO-155,
/// <c>docs/v1.8/Investment_Proposal_Spec.md</c> §4).
///
/// <para>
/// Every route is JWT-protected and gated on <c>CanAccessBudgetPlanning</c>. Every write goes
/// through <see cref="ConfigHttp.AuthorizeWriteAsync"/>, so a cross-office (comment-only) reviewer
/// is refused before the service runs. Office and division scope are the service's, which answers
/// 404 for out-of-scope and missing alike.
/// </para>
///
/// <para>
/// ⚠️ Route ordering: <c>proposals/projects</c> is a literal segment and every <c>{id}</c> route is
/// constrained to <c>:int</c>, so "projects" can never bind as an id.
/// </para>
/// </summary>
public sealed class InvestmentProposalFunctions
{
    private readonly IInvestmentProposalService _proposals;
    private readonly IJwtValidator              _jwt;
    private readonly IPermissionService         _permissions;

    public InvestmentProposalFunctions(
        IInvestmentProposalService proposals, IJwtValidator jwt, IPermissionService permissions)
    {
        _proposals   = proposals;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    private static int? OfficeIdOf(NameValueCollection q)
        => int.TryParse(q["officeId"], out int id) && id > 0 ? id : null;

    private static int IntOr(NameValueCollection q, string key, int fallback)
        => int.TryParse(q[key], out int v) ? v : fallback;

    // ── GET /api/budget-planning/proposals?fiscalYear=&officeId=&search=&page=&pageSize= ──
    [Function("InvestmentProposalList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/proposals")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        NameValueCollection q = HttpUtility.ParseQueryString(req.Url.Query);
        if (!int.TryParse(q["fiscalYear"], out int fiscalYear) || fiscalYear <= 0)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ProposalListPageDto>.Fail("fiscalYear is required."), ct);

        return await ConfigHttp.FromResultAsync(req, await _proposals.ListAsync(
            fiscalYear, OfficeIdOf(q), q["search"], IntOr(q, "page", 1), IntOr(q, "pageSize", 25), caller!, ct), ct);
    }

    // ── GET /api/budget-planning/proposals/projects?fiscalYear=&officeId= ──
    // The editor's picker and AIP Entry's strip (decision 29): one office, unpaged.
    [Function("InvestmentProposalProjects")]
    public async Task<HttpResponseData> Projects(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/proposals/projects")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        NameValueCollection q = HttpUtility.ParseQueryString(req.Url.Query);
        if (!int.TryParse(q["fiscalYear"], out int fiscalYear) || fiscalYear <= 0)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<IReadOnlyList<ProposalProjectOptionDto>>.Fail("fiscalYear is required."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _proposals.ListProjectOptionsAsync(fiscalYear, OfficeIdOf(q), caller!, ct), ct);
    }

    // ── POST /api/budget-planning/proposals — body { aipProjectId } ──
    [Function("InvestmentProposalCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/proposals")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        CreateProposalDto? body = await ConfigHttp.ReadBodyAsync<CreateProposalDto>(req, ct);
        if (body is null || body.AipProjectId <= 0)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ProposalDto>.Fail("aipProjectId is required."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _proposals.CreateAsync(body.AipProjectId, caller!, ct), ct, HttpStatusCode.Created);
    }

    // ── GET /api/budget-planning/proposals/{id} ──
    [Function("InvestmentProposalGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "budget-planning/proposals/{id:int}")] HttpRequestData req,
        int id,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;
        return await ConfigHttp.FromResultAsync(req, await _proposals.GetAsync(id, caller!, ct), ct);
    }

    // ── PUT /api/budget-planning/proposals/{id} — body { rowVersion, content } ──
    [Function("InvestmentProposalUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "budget-planning/proposals/{id:int}")] HttpRequestData req,
        int id,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        UpdateProposalDto? body = await ConfigHttp.ReadBodyAsync<UpdateProposalDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<ProposalDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req, await _proposals.UpdateAsync(id, body, caller!, ct), ct);
    }

    // ── POST /api/budget-planning/proposals/{id}/finalize — body { rowVersion } ──
    [Function("InvestmentProposalFinalize")]
    public async Task<HttpResponseData> Finalize(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/proposals/{id:int}/finalize")] HttpRequestData req,
        int id,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        RowVersionDto? body = await ConfigHttp.ReadBodyAsync<RowVersionDto>(req, ct);
        return await ConfigHttp.FromResultAsync(req,
            await _proposals.FinalizeAsync(id, body?.RowVersion, caller!, ct), ct, message: "Proposal finalized");
    }

    // ── POST /api/budget-planning/proposals/{id}/reopen — body { rowVersion } ──
    // CanReopenInvestmentProposal is per office, so the service checks it once it knows the office.
    [Function("InvestmentProposalReopen")]
    public async Task<HttpResponseData> Reopen(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "budget-planning/proposals/{id:int}/reopen")] HttpRequestData req,
        int id,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        RowVersionDto? body = await ConfigHttp.ReadBodyAsync<RowVersionDto>(req, ct);
        return await ConfigHttp.FromResultAsync(req,
            await _proposals.ReopenAsync(id, body?.RowVersion, caller!, ct), ct, message: "Proposal reopened");
    }

    // ── DELETE /api/budget-planning/proposals/{id}?rowVersion= ──
    [Function("InvestmentProposalDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "budget-planning/proposals/{id:int}")] HttpRequestData req,
        int id,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeWriteAsync(req, _jwt, _permissions, CanAccess, ct);
        if (denied is not null) return denied;

        // Base64 carries '+', which query parsing reads as a space when the client did not encode it.
        string? rowVersion = HttpUtility.ParseQueryString(req.Url.Query)["rowVersion"]?.Replace(' ', '+');
        ServiceResult<bool> result = await _proposals.DeleteAsync(id, rowVersion, caller!, ct);
        return result.IsSuccess
            ? req.CreateResponse(HttpStatusCode.NoContent)
            : await ConfigHttp.FromResultAsync(req, result, ct);
    }
}
