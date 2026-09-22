using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// Shared helpers for the config CRUD endpoints (RAL-70): the <c>{ data, error, message }</c>
/// envelope, ServiceResult → HTTP status mapping, JSON options, and body/header reads.
/// </summary>
internal static class ConfigHttp
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    };

    internal static string? AuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>Any authenticated user — used for reference-data list endpoints (dropdowns).</summary>
    internal static readonly Func<User, Task<bool>> Authenticated = _ => Task.FromResult(true);

    /// <summary>
    /// Validates the JWT and the supplied permission predicate.
    /// Returns the caller on success, or a 401/403 response to short-circuit on failure.
    /// </summary>
    internal static async Task<(User? caller, HttpResponseData? denied)> AuthorizeAsync(
        HttpRequestData req,
        IJwtValidator jwt,
        Func<User, Task<bool>> permit,
        CancellationToken cancellationToken)
    {
        User? caller = await jwt.ValidateAsync(AuthHeader(req), cancellationToken);
        if (caller is null)
            return (null, req.CreateResponse(HttpStatusCode.Unauthorized));
        if (!await permit(caller))
            return (null, req.CreateResponse(HttpStatusCode.Forbidden));
        return (caller, null);
    }

    /// <summary>
    /// <see cref="AuthorizeAsync"/> plus the reviewer write-denial (v1.8.0 — RAL-256).
    /// <b>Use this on every budget-planning endpoint that WRITES content</b>; keep
    /// <see cref="AuthorizeAsync"/> for reads.
    ///
    /// The additive predicate and the subtractive guard are two different questions and both have
    /// to be asked: <paramref name="permit"/> answers "may this caller use this feature at all?",
    /// and <see cref="ReviewerWriteGuard"/> answers "is this caller a comment-only reviewer?".
    /// A predicate cannot express the second — returning false from it is indistinguishable from
    /// a missing grant — which is why this wrapper exists rather than a cleverer predicate.
    ///
    /// Order matters: the permission check runs first, so a caller who cannot reach the feature
    /// still gets the same 403 they always did, and the denial never becomes an oracle for who
    /// holds a reviewer flag.
    ///
    /// The rule is not "reviewers cannot write" — read <see cref="ReviewerWriteGuard"/> before
    /// changing anything here. The department-head reviewer may edit; only the cross-office
    /// consolidated reviewer is denied.
    /// </summary>
    internal static async Task<(User? caller, HttpResponseData? denied)> AuthorizeWriteAsync(
        HttpRequestData req,
        IJwtValidator jwt,
        IPermissionService permissions,
        Func<User, Task<bool>> permit,
        CancellationToken cancellationToken)
    {
        (User? caller, HttpResponseData? denied) = await AuthorizeAsync(req, jwt, permit, cancellationToken);
        if (denied is not null) return (null, denied);

        if (await ReviewerWriteGuard.DeniesWriteAsync(caller!, permissions, cancellationToken))
            return (null, req.CreateResponse(HttpStatusCode.Forbidden));

        return (caller, null);
    }

    // ── Office scoping (v1.8.0 — RAL-228) ─────────────────────────────────────
    // Thin wrappers over OfficeScope so endpoints don't re-implement the rule. The logic and
    // its tests live in PPDO.Application/Common/OfficeScope.cs — read its XML doc before
    // changing either of these.
    //
    // ⚠️ This comment used to say a null OfficeId means PPDO-internal (full access) here but
    // "see nothing" in DivisionScope. DECISION F (RAL-258) retired that inversion: cross-office
    // authority is Office.IsHostOffice, and a null office id now means unassigned — sees nothing
    // — on both axes. Corrected in RAL-256; see BudgetPlanningScope for the two rules side by side.

    /// <summary>
    /// Clamps a caller-supplied office id to what the caller may actually use: an office user
    /// always gets their own office whatever they asked for, a PPDO caller gets the requested
    /// value through unchanged.
    ///
    /// Prefer this over validate-and-reject — there is no error path to get wrong, and a client
    /// cannot probe for other offices' ids by watching which ones 403.
    /// </summary>
    internal static int? ClampOfficeId(User caller, int? requestedOfficeId)
        => OfficeScope.Resolve(caller).Clamp(requestedOfficeId);

    /// <summary>
    /// <see cref="ClampOfficeId"/> for the <b>allocation-setup reads</b> (v1.8.0 — PPDO-18): same
    /// clamp, except that a holder of <c>CanManageOfficeCeilings</c> keeps the office id they asked
    /// for. That grant is authority over every office's ceiling (RAL-243), so a plain clamp would
    /// make it unreachable — the holder could only ever load their own office.
    ///
    /// <paramref name="canManageOfficeCeilings"/> must come from
    /// <c>IPermissionService.CanManageOfficeCeilingsAsync</c>. Reads only, plus the ceiling write the
    /// grant itself covers; see <see cref="OfficeScope.ResolveForCeiling"/> for why it is a
    /// separate entry point and must not be used on the other write paths.
    /// </summary>
    internal static int? ClampOfficeIdForCeiling(User caller, bool canManageOfficeCeilings, int? requestedOfficeId)
        => OfficeScope.ResolveForCeiling(caller, canManageOfficeCeilings).Clamp(requestedOfficeId);

    /// <summary>
    /// Returns a 403 when an office-scoped caller targets a record owned by a different office,
    /// or a record with no owning office (PPDO-only, e.g. LDIP's multi-office bulk uploads).
    /// Returns null when the caller may proceed.
    ///
    /// Use this only where the record's owning office is already known and refusing is the
    /// correct answer; for caller-supplied ids on list/query endpoints use
    /// <see cref="ClampOfficeId"/> instead. Generalises <c>LdipFunctions.DenyForeignOfficeAsync</c>.
    /// </summary>
    internal static HttpResponseData? DenyForeignOffice(
        HttpRequestData req, User caller, int? owningOfficeId)
        => OfficeScope.Resolve(caller).Permits(owningOfficeId)
            ? null
            : req.CreateResponse(HttpStatusCode.Forbidden);

    /// <summary>
    /// Reads and deserializes a JSON request body, returning <c>default</c> when it is missing or
    /// malformed so the handler can answer 400. The single copy of what used to be a private
    /// <c>DeserializeAsync&lt;T&gt;</c> in ten other Function files (PPDO-43).
    /// </summary>
    /// <param name="options">
    /// ⚠️ <b>Pass the caller's own options whenever it has any.</b> The ten copies this replaced
    /// were <i>not</i> interchangeable, which is the trap here: all used camelCase, but only four
    /// set <c>PropertyNameCaseInsensitive</c>, and <c>AnnouncementFunctions</c> and
    /// <c>DashboardFunctions</c> additionally register a <c>JsonStringEnumConverter</c>.
    /// Defaulting those two to <see cref="Json"/> would stop <c>AnnouncementStatus</c> and
    /// <c>CalendarEventStatus</c> deserializing from strings — and because the failure is
    /// swallowed below it would not surface as a parse error, but as a silent null body and a
    /// baffling 400. Omit this only when <see cref="Json"/> is genuinely what you want.
    /// </param>
    internal static async Task<T?> ReadBodyAsync<T>(
        HttpRequestData req,
        CancellationToken cancellationToken,
        JsonSerializerOptions? options = null)
    {
        // Bare catch preserved from the ten originals rather than narrowed to JsonException.
        // Narrowing is the better end state — it would let genuinely unexpected failures reach
        // the new exception stage and be logged instead of masquerading as a malformed body —
        // but it changes the failure status of every write endpoint, which is not a change to
        // make while v1.8.0 is soaking in UAT. Noted on PPDO-43 as a follow-up.
        try { return await JsonSerializer.DeserializeAsync<T>(req.Body, options ?? Json, cancellationToken); }
        catch { return default; }
    }

    /// <summary>
    /// Decodes a base64 <c>rowversion</c> from a request body (V18-71 / PPDO-119).
    /// <c>ok: false</c> means the value was present but not valid base64 — a 400, not a silent
    /// null.
    /// </summary>
    /// <remarks>
    /// ⚠️ A <b>missing</b> value (null/empty) returns <c>ok: true</c> with null bytes, and the save
    /// then proceeds <b>unguarded</b>. That is the staged-rollout state described in
    /// <c>docs/v1.8/AIP_Concurrent_Edit_Spec.md</c> §8, not the end state: while any caller can
    /// omit the field, the concurrency guard is opt-out by omission. PPDO-121 turns the omission
    /// into a 400 and is the ticket that actually switches the protection on.
    /// </remarks>
    internal static (bool ok, byte[]? bytes) DecodeRowVersion(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64)) return (true, null);

        // Convert.FromBase64String throws on malformed input; TryFromBase64Chars does not, and a
        // malformed token is a client bug worth reporting rather than an exception worth throwing.
        byte[] buffer = new byte[((base64.Length * 3) + 3) / 4];
        return Convert.TryFromBase64Chars(base64, buffer, out int written)
            ? (true, buffer[..written])
            : (false, null);
    }

    internal static async Task<string> ReadTextAsync(HttpRequestData req)
    {
        using StreamReader reader = new(req.Body);
        return await reader.ReadToEndAsync();
    }

    internal static async Task<HttpResponseData> EnvelopeAsync<T>(
        HttpRequestData req, HttpStatusCode status, ApiResponse<T> body, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, Json), cancellationToken);
        return response;
    }

    /// <summary>Maps a <see cref="ServiceResult{T}"/> to an enveloped HTTP response.</summary>
    internal static Task<HttpResponseData> FromResultAsync<T>(
        HttpRequestData req,
        ServiceResult<T> result,
        CancellationToken cancellationToken,
        HttpStatusCode okStatus = HttpStatusCode.OK,
        string? message = null)
    {
        if (result.IsSuccess)
            return EnvelopeAsync(req, okStatus, ApiResponse<T>.Ok(result.Value!, message), cancellationToken);

        HttpStatusCode status = result.Code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.Conflict   => HttpStatusCode.Conflict,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            ServiceErrorCode.Forbidden  => HttpStatusCode.Forbidden,
            _                           => HttpStatusCode.InternalServerError,
        };

        // A failure carrying structured detail puts it in `data` so the client can act on it
        // rather than only read a sentence (V18-71 / PPDO-119). Today that is the AIP concurrency
        // conflict, which needs the current values and the new rowversion to offer Overwrite.
        // Typed as ApiResponse<object> because Details is deliberately not T — the envelope shape
        // is unchanged, only what rides in `data`.
        if (result.Details is not null)
            return EnvelopeAsync(req, status,
                new ApiResponse<object>(result.Details, result.Error ?? "An unexpected error occurred.", null),
                cancellationToken);

        return EnvelopeAsync(req, status, ApiResponse<T>.Fail(result.Error ?? "An unexpected error occurred."), cancellationToken);
    }

    /// <summary>Returns a CSV file response (text/csv + attachment filename).</summary>
    internal static async Task<HttpResponseData> CsvFileAsync(
        HttpRequestData req, string csv, string fileName, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/csv; charset=utf-8");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        await response.WriteStringAsync(csv, cancellationToken);
        return response;
    }
}
