using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// SAIP start, cancel, list and get (v1.10.0 — PPDO-246, <c>docs/v1.10/SAIP_Spec.md</c> §4).
///
/// <para>
/// All four are gated on <c>CanAccessBudgetPlanning</c>, like the AIP's own history read. Office
/// scope (404) and "department head only" (403) are the service's: the head is not a flag, it is
/// Admin or <c>CanReviewBudgetPlanning</c> <b>in that office</b>, which only the service can test.
/// </para>
///
/// <para>
/// ⚠️ Start and cancel use <c>AuthorizeAsync</c>, not <c>AuthorizeWriteAsync</c>, for the reason
/// <c>AipSubmitFunctions.SubmitToPpdo</c> gives: they move your own office's workflow, they are not
/// content edits, and the reviewer write-denial would refuse a head who also holds a review flag.
/// </para>
///
/// <para>
/// ↩️ The routes take the AIP record id (<c>{aipId}</c>), like every other AIP route, where the spec
/// sketched <c>{fy}</c>. There is one live record per fiscal year, so the two name the same thing.
/// </para>
/// </summary>
public sealed class SaipFunctions
{
    private readonly ISaipService       _saips;
    private readonly IJwtValidator      _jwt;
    private readonly IPermissionService _permissions;

    public SaipFunctions(ISaipService saips, IJwtValidator jwt, IPermissionService permissions)
    {
        _saips       = saips;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanAccess(User u) => _permissions.CanAccessBudgetPlanningAsync(u);

    // ── POST /api/budget-planning/aip/{aipId}/offices/{officeId}/saips ────────
    [Function("SaipStart")]
    public async Task<HttpResponseData> Start(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        // No body: what is copied is the office's approved AIP, decided by the route alone.
        return await ConfigHttp.FromResultAsync(req,
            await _saips.StartAsync(aipId, officeId, caller!, ct), ct, System.Net.HttpStatusCode.Created);
    }

    // ── GET /api/budget-planning/aip/{aipId}/offices/{officeId}/saips ─────────
    [Function("SaipList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips")] HttpRequestData req,
        int aipId, int officeId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _saips.ListAsync(aipId, officeId, caller!, ct), ct);
    }

    // ── GET /api/budget-planning/aip/{aipId}/offices/{officeId}/saips/{saipId} ─
    [Function("SaipGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips/{saipId:int}")] HttpRequestData req,
        int aipId, int officeId, int saipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _saips.GetAsync(aipId, officeId, saipId, caller!, ct), ct);
    }

    // ── POST /api/budget-planning/aip/{aipId}/offices/{officeId}/saips/{saipId}/cancel ──
    [Function("SaipCancel")]
    public async Task<HttpResponseData> Cancel(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips/{saipId:int}/cancel")] HttpRequestData req,
        int aipId, int officeId, int saipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _saips.CancelAsync(aipId, officeId, saipId, caller!, ct), ct);
    }

    // ── PUT /api/budget-planning/aip/{aipId}/offices/{officeId}/saips/{saipId}/letter?fileName= ──
    // PPDO-247. Body: the raw file bytes, as every upload in this API takes them (the AIP and LDIP
    // uploads); the name rides in ?fileName=. ↩️ The spec sketched multipart — one file and one name
    // need no form encoding, and the frontend sends a File as the body directly. The type is read
    // from the bytes by the service, never from the request's Content-Type.
    [Function("SaipPutLetter")]
    public async Task<HttpResponseData> PutLetter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips/{saipId:int}/letter")] HttpRequestData req,
        int aipId, int officeId, int saipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        byte[] content = await ReadCappedAsync(req.Body, SaipLetterRules.MaxBytes + 1, ct);
        return await ConfigHttp.FromResultAsync(req,
            await _saips.PutLetterAsync(aipId, officeId, saipId, req.Query["fileName"], content, caller!, ct), ct);
    }

    // ── GET /api/budget-planning/aip/{aipId}/offices/{officeId}/saips/{saipId}/letter ──
    [Function("SaipGetLetter")]
    public async Task<HttpResponseData> GetLetter(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips/{saipId:int}/letter")] HttpRequestData req,
        int aipId, int officeId, int saipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        ServiceResult<SaipLetterFile> result = await _saips.GetLetterAsync(aipId, officeId, saipId, caller!, ct);
        // A refusal is an envelope the page can read, never a file.
        if (!result.IsSuccess) return await ConfigHttp.FromResultAsync(req, result, ct);

        // ⚠️ Headers before the body: one added after the write is silently dropped on the real host.
        HttpResponseData response = req.CreateResponse(System.Net.HttpStatusCode.OK);
        response.Headers.Add("Content-Type", result.Value!.ContentType);
        response.Headers.Add("Content-Disposition", InvestmentProposalFunctions.ContentDisposition(result.Value.FileName));
        response.Headers.Add("X-Content-Type-Options", "nosniff");
        await response.WriteBytesAsync(result.Value.Content, ct);
        return response;
    }

    // ── POST /api/budget-planning/aip/{aipId}/offices/{officeId}/saips/{saipId}/submit ──
    // Like start and cancel: AuthorizeAsync, not AuthorizeWriteAsync (see the class comment).
    [Function("SaipSubmit")]
    public async Task<HttpResponseData> Submit(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips/{saipId:int}/submit")] HttpRequestData req,
        int aipId, int officeId, int saipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _saips.SubmitAsync(aipId, officeId, saipId, caller!, ct), ct);
    }

    // ── GET /api/budget-planning/aip/{aipId}/offices/{officeId}/saips/{saipId}/changes ──
    [Function("SaipChanges")]
    public async Task<HttpResponseData> Changes(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "budget-planning/aip/{aipId:int}/offices/{officeId:int}/saips/{saipId:int}/changes")] HttpRequestData req,
        int aipId, int officeId, int saipId, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) =
            await ConfigHttp.AuthorizeAsync(req, _jwt, CanAccess, ct);
        if (denied is not null) return denied;

        return await ConfigHttp.FromResultAsync(req,
            await _saips.GetChangesAsync(aipId, officeId, saipId, caller!, ct), ct);
    }

    /// <summary>
    /// Reads at most <paramref name="limit"/> bytes of the body. One byte over the letter cap is
    /// enough for the service to answer "too large" without buffering a huge upload in memory.
    /// </summary>
    private static async Task<byte[]> ReadCappedAsync(Stream body, long limit, CancellationToken ct)
    {
        using MemoryStream buffer = new();
        byte[] chunk = new byte[81920];
        int read;
        while (buffer.Length < limit
               && (read = await body.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, limit - buffer.Length)), ct)) > 0)
            buffer.Write(chunk, 0, read);
        return buffer.ToArray();
    }
}
