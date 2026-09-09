using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// The PPDO consolidated reviewer's actions on another office's AIP (V18-54 / PPDO-72,
/// <c>AIP_Review_Spec.md</c> §4).
///
/// <para>
/// ⚠️ <b><c>AuthorizeAsync</c>, never <c>AuthorizeWriteAsync</c>.</b> <c>ReviewerWriteGuard</c>
/// denies budget-planning content writes to holders of <c>CanReviewAllOffices</c> — which is
/// exactly the flag this route requires — so routing it through that wrapper would deny the action
/// to the only role that may perform it. The guard's own remarks name "returning a submission" as
/// something that must not go through it, and <c>AipSubmitFunctions.SubmitToPpdo</c> carries the
/// same note for the same reason. What the guard protects is another office's <i>numbers</i>;
/// returning moves a workflow column and touches no content.
/// </para>
///
/// <para>
/// ⚠️ <b>There is no <c>AipWriteGuard</c> here either.</b> That guard closes an office once it
/// reaches PPDO — which is the only state a return can start from. Applying it would make the
/// action refuse itself.
/// </para>
///
/// <para>
/// The gate is <c>CanReviewAllOffices</c> and nothing weaker. ⚠️ In particular it is <b>not</b>
/// "is this caller in the host office": a PPDO division encoder is in the host office and has no
/// business returning another office's work. The service checks the same flag again on its own
/// account rather than trusting this one — see <c>AipReviewService.ResolveAsync</c>.
/// </para>
/// </summary>
public sealed class AipReviewFunctions
{
    private readonly IAipReviewService  _review;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public AipReviewFunctions(
        IAipReviewService  review,
        IJwtMiddleware     jwt,
        IPermissionService permissions)
    {
        _review      = review;
        _jwt         = jwt;
        _permissions = permissions;
    }

    /// <summary>The cross-office consolidated reviewer's grant (RAL-257).</summary>
    private Task<bool> CanReviewAllOffices(User u) => _permissions.CanReviewAllOfficesAsync(u);

    // ── POST /api/budget-planning/aip/{aipId}/offices/{officeId}/return ───────
    //
    // No body. A covering note is deliberately not part of this action — the row-anchored comments
    // (PPDO-71) are how a reviewer says what needs to change, and a second free-text channel
    // attached to the transition would compete with them.
    [Function("AipReviewReturn")]
    public async Task<HttpResponseData> Return(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/return")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanReviewAllOffices, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _review.ReturnToOfficeAsync(aipId, officeId, caller!, ct), ct);
    }

    // ── POST /api/budget-planning/aip/{aipId}/offices/{officeId}/accept ───────
    //
    // The other half of the reviewer's decision (PPDO-74): SubmittedToPpdo → Consolidated, one
    // office at a time. Same gate, same no-body shape and the same guard-free treatment as Return —
    // it moves a workflow column and touches no figures.
    [Function("AipReviewAccept")]
    public async Task<HttpResponseData> Accept(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/accept")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanReviewAllOffices, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _review.AcceptOfficeAsync(aipId, officeId, caller!, ct), ct);
    }

    // ── GET /api/budget-planning/aip/{aipId}/offices/{officeId}/review ────────
    //
    // ⚠️ Gated on CanReviewAllOffices like the two actions, NOT on CanAccessBudgetPlanning. This is
    // the reviewer's surface; an office reads its own AIP on the entry page, which carries the
    // editability rules that belong to it. Widening this to "the office may read itself here too"
    // would give one record two reads under two different scope resolvers, which is how the two
    // drift apart.
    [Function("AipReviewOfficeRead")]
    public async Task<HttpResponseData> GetForReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/review")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanReviewAllOffices, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _review.GetOfficeForReviewAsync(aipId, officeId, caller!, ct), ct);
    }
}
