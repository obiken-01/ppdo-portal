using System.Collections.Specialized;
using System.Net;
using System.Web;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
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
    private readonly IJwtValidator     _jwt;
    private readonly IPermissionService _permissions;

    public AipReviewFunctions(
        IAipReviewService  review,
        IJwtValidator     jwt,
        IPermissionService permissions)
    {
        _review      = review;
        _jwt         = jwt;
        _permissions = permissions;
    }

    /// <summary>The cross-office consolidated reviewer's grant (RAL-257).</summary>
    private Task<bool> CanReviewAllOffices(User u) => _permissions.CanReviewAllOfficesAsync(u);

    /// <summary>
    /// The ordinary budget-planning grant — the gate on the SEARCH only (PPDO-76).
    ///
    /// ⚠️ Weaker than every other route on this class, on purpose: the search clamps a guest office
    /// to its own rows rather than refusing it, so that a 403 cannot be used to discover which
    /// offices exist (§3.4, PPDO-46).
    /// </summary>
    private Task<bool> CanAccessBudgetPlanning(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    /// <summary>
    /// Either reviewer — the gate on the activity modal's read (PPDO-79).
    ///
    /// ⚠️ A flag check, so a 403 here says only "you are not a reviewer", never anything about which
    /// offices exist. Which office's activity a department head may open is the service's question,
    /// and it answers with a 404 worded like a missing activity (PPDO-46).
    /// </summary>
    private async Task<bool> IsEitherReviewer(User u)
        => await _permissions.CanReviewAllOfficesAsync(u)
        || await _permissions.CanReviewBudgetPlanningAsync(u);

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

    // ── POST /api/budget-planning/aip/{aipId}/offices/{officeId}/reopen ───────
    //
    // Added 2026-09-14 with PPDO-73: Consolidated → ReturnedByPpdo. Same gate, same no-body shape and
    // the same guard-free treatment as Return and Accept. A route of its own rather than Return
    // widened, so a stale screen cannot re-open an office a colleague has just accepted.
    [Function("AipReviewReopen")]
    public async Task<HttpResponseData> Reopen(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/reopen")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanReviewAllOffices, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _review.ReopenOfficeAsync(aipId, officeId, caller!, ct), ct);
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

    // ── GET /api/budget-planning/aip/activities/{activityId}/review ───────────
    //
    // PPDO-79. One activity, with its path, its expenditure lines and whether this caller may edit
    // it — the modal an activity name opens on the AIP Review search.
    //
    // ⚠️ Open to BOTH reviewers, unlike the whole-office read above. The department head reviews
    // their own office from the same search page (spec §6.1a), and this is how they open a row. The
    // service narrows a department head to their own office; this gate only keeps non-reviewers out.
    //
    // ⚠️ AuthorizeAsync, not AuthorizeWriteAsync — it is a read.
    [Function("AipReviewActivityRead")]
    public async Task<HttpResponseData> GetActivityForReview(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/activities/{activityId:int}/review")] HttpRequestData req,
        int activityId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, IsEitherReviewer, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _review.GetActivityForReviewAsync(activityId, caller!, ct), ct);
    }

    // ── GET /api/budget-planning/aip/review/search ────────────────────────────
    //
    // V18-75 / PPDO-76, spec §4.1. The query-first review page.
    //
    // ⚠️ Gated on CanAccessBudgetPlanning, NOT on the reviewer flag, and that is deliberate: §3.4
    // says a guest office asking about somebody else is CLAMPED to its own rows rather than
    // refused, because a 403 would confirm the other office exists (PPDO-46). The clamp lives in
    // the service, where the scope resolver is.
    //
    // ⚠️ The PAGE is gated more tightly than this route — it is the review surface, so the client
    // hides it from anyone holding neither reviewer flag (PPDO-79 opened it to department heads).
    // That is a UI decision about dead ends, not a security boundary, and it does not belong here.
    //
    // ⚠️ Route ordering: "review" is not an int, so this cannot collide with
    // budget-planning/aip/{aipId:int} — the constraint is what keeps them apart.
    [Function("AipReviewSearch")]
    public async Task<HttpResponseData> Search(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/review/search")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccessBudgetPlanning, ct);
        if (denied is not null) return denied;

        NameValueCollection q = HttpUtility.ParseQueryString(req.Url.Query);

        AipReviewSearchRequestDto request = new(
            FiscalYear:       TryInt(q["fiscalYear"], 0),
            OfficeIds:        SplitInts(q["officeIds"]),
            Sectors:          SplitStrings(q["sectors"]),
            WorkflowStatuses: SplitStrings(q["workflowStatuses"]),
            // ⚠️ Passed through raw. The OR-list splitting is the service's job — doing it here
            // would put a second parser on the boundary, and the title must never meet one at all.
            RefCode:          q["refCode"],
            Title:            q["title"],
            Mine:             string.Equals(q["mine"], "true", StringComparison.OrdinalIgnoreCase),
            Page:             TryInt(q["page"], 1),
            PageSize:         TryInt(q["pageSize"], 25));

        if (request.FiscalYear <= 0)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<AipReviewSearchResultDto>.Fail("fiscalYear is required."), ct);

        return await ConfigHttp.FromResultAsync(req,
            await _review.SearchAsync(request, caller!, ct), ct);
    }

    private static int TryInt(string? raw, int fallback)
        => int.TryParse(raw, out int value) ? value : fallback;

    /// <summary>
    /// A repeated multi-select value, sent as one comma-separated parameter.
    ///
    /// ⚠️ A comma here is only a transport separator for a list the client already holds as chips —
    /// it is not the typed "OR" list, which reaches the service unparsed and is split there.
    /// </summary>
    private static List<string> SplitStrings(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                 .Select(v => v.Trim())
                 .Where(v => v.Length > 0)
                 .ToList();

    /// <summary>
    /// The same, as ids. ⚠️ Unparseable entries are dropped rather than failing the request: a
    /// malformed id cannot widen the result — scope is applied afterwards regardless — and a 400
    /// here would break the page on a stale bookmark.
    /// </summary>
    private static List<int> SplitInts(string? raw)
        => SplitStrings(raw)
            .Select(v => int.TryParse(v, out int id) ? id : (int?)null)
            .Where(id => id is not null)
            .Select(id => id!.Value)
            .ToList();
}
