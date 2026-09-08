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
/// Inline review comments on AIP rows (V18-53 / PPDO-71, <c>AIP_Review_Spec.md</c> §4).
///
/// <para>
/// ⚠️ <b>All three use <c>AuthorizeAsync</c>, never <c>AuthorizeWriteAsync</c>, and that is
/// deliberate.</b> <c>ReviewerWriteGuard</c> denies content writes to cross-office reviewers — who
/// are the main authors here — so routing a comment through it would deny the action to exactly
/// the people the feature is for. The guard's own remarks call this out by name: "a comment-only
/// reviewer who cannot comment is not a reviewer."
/// </para>
///
/// <para>
/// ⚠️ <b>There is also no <c>AipWriteGuard</c> here.</b> That guard closes an office once it
/// reaches PPDO, which is precisely the state in which PPDO needs to comment on it. A comment is
/// not a content write.
/// </para>
///
/// <para>
/// The gate is <c>CanAccessBudgetPlanning</c> — the same one the entry page uses, because an
/// encoder must be able to <i>read</i> the comments addressed to them. Who may <i>write</i> and who
/// may <i>resolve</i> is decided in the service, where the side rules live.
/// </para>
/// </summary>
public sealed class AipReviewCommentFunctions
{
    private readonly IAipReviewCommentService _comments;
    private readonly IJwtMiddleware           _jwt;
    private readonly IPermissionService       _permissions;

    public AipReviewCommentFunctions(
        IAipReviewCommentService comments,
        IJwtMiddleware           jwt,
        IPermissionService       permissions)
    {
        _comments    = comments;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── GET /api/budget-planning/aip/{aipId}/offices/{officeId}/comments ──────
    [Function("AipReviewCommentsList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/comments")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _comments.GetForOfficeAsync(aipId, officeId, caller!, ct), ct);
    }

    // ── POST /api/budget-planning/aip/{aipId}/offices/{officeId}/comments ─────
    [Function("AipReviewCommentCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/comments")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        CreateAipReviewCommentDto? body =
            await ConfigHttp.ReadBodyAsync<CreateAipReviewCommentDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipReviewCommentDto>.Fail("Request body is missing or malformed."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _comments.CreateAsync(aipId, officeId, body, caller!, ct), ct);
    }

    // ── POST /api/budget-planning/aip/comments/{id}/resolve ──────────────────
    // ⚠️ Refused for the side the comment is addressed to — see the service. The client hides the
    // control on CanResolve=false; this is what actually enforces it.
    [Function("AipReviewCommentResolve")]
    public async Task<HttpResponseData> Resolve(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/comments/{id:int}/resolve")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _comments.ResolveAsync(id, caller!, ct), ct);
    }
}
