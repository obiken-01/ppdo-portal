using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// Funding source config endpoints (<c>/api/config/funding-sources</c>) — RAL-70.
/// Responses use the <c>{ data, error, message }</c> envelope. Soft delete only.
///
/// ↩️ <b>PPDO-109 splits the table in two and adds a second, narrower writer.</b> A fund with no
/// office is province-wide and PPDO's; a fund with an office belongs to that office alone (D5). A
/// department head holding <c>CanManageOfficeSetup</c> manages their OWN office's funds beside the
/// shared ones. Four rules make that safe, and all four are enforced HERE:
/// <list type="number">
///   <item><description><b>Reads are clamped, not refused.</b> Every caller sees the shared funds
///   plus at most one office's own — their own, or the office they asked for when they have
///   cross-office authority. This is why <c>?officeId=</c> exists: the WFP/AIP fund picker resolves
///   it from the RECORD being edited, so a PPDO reviewer opening another office's AIP sees that
///   office's funds rather than their own.</description></item>
///   <item><description><b>A shared row is read-only</b> to an office-scoped caller — 403, whether
///   they aimed at it by id or not. PPDO maintains the province-wide list.</description></item>
///   <item><description><b>Creates are stamped</b> with the caller's own office; a body office id is
///   ignored rather than rejected, for the same reason PPDO-108 drops the division flags — an older
///   client that still sends one must not break, and the field is not theirs to set either way.
///   Ownership is then fixed for the row's life; <c>FundingSourceService.UpdateAsync</c> does not
///   read <c>OfficeId</c> off the body at all.</description></item>
///   <item><description><b>The CSV routes stay <c>CanManageConfig</c>-only</b>, exactly as in
///   PPDO-108: a bulk upsert keyed by code spans every office's funds, so an office-scoped caller
///   has no safe reading of it.</description></item>
/// </list>
///
/// ⚠️ Codes stay globally unique (D6), so a department head adding <c>GF</c> is told the code is
/// taken — by the ordinary 409 in the service, needing nothing special here.
/// </summary>
public sealed class ConfigFundingSourceFunctions
{
    private readonly IFundingSourceService _funding;
    private readonly IJwtMiddleware        _jwt;
    private readonly IPermissionService    _permissions;

    public ConfigFundingSourceFunctions(IFundingSourceService funding, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _funding     = funding;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u) => _permissions.CanManageConfigAsync(u);

    /// <summary>Config managers, plus a department head for their own office (PPDO-109).</summary>
    private async Task<bool> CanManageFundingSources(User u)
        => await _permissions.CanManageConfigAsync(u)
        || await _permissions.CanManageOfficeSetupAsync(u);

    /// <summary>
    /// True for a caller whose writes are office-checked and whose reads are clamped.
    ///
    /// ⚠️ Asked as "not a config manager" rather than "holds the setup grant" — the same reasoning as
    /// <c>ConfigDivisionFunctions.IsOfficeScopedAsync</c>: someone holding both grants IS a config
    /// manager and must keep the full page, otherwise giving a PPDO admin the new flag would quietly
    /// take away every other office's funds.
    /// </summary>
    private async Task<bool> IsOfficeScopedAsync(User u, CancellationToken ct)
        => !await _permissions.CanManageConfigAsync(u, ct);

    /// <summary>
    /// The office whose funds this caller may see alongside the shared ones, honouring
    /// <c>?officeId=</c> only as far as the caller's own scope allows. Null means no filter — a
    /// config manager's cross-office view.
    ///
    /// Clamped rather than validated, per <c>OfficeScope.Clamp</c>: there is no error path to get
    /// wrong, and a client cannot probe for other offices' ids by watching which ones 403.
    /// </summary>
    private async Task<int?> VisibleOfficeIdAsync(User caller, HttpRequestData req, CancellationToken ct)
    {
        int? requested = int.TryParse(req.Query["officeId"], out int oid) ? oid : null;

        // A config manager reading with no office named wants the whole table — the config page.
        // With one named they want that office's view, which is what the clamp already returns.
        if (!await IsOfficeScopedAsync(caller, ct) && requested is null)
            return null;

        return ConfigHttp.ClampOfficeId(caller, requested);
    }

    /// <summary>
    /// 403 unless the fund exists AND belongs to the caller's own office. Only called for an
    /// office-scoped caller; a config manager never reaches it.
    ///
    /// ⚠️ Covers both refusals PPDO-109 asks for in one place: a SHARED fund (no office — PPDO's to
    /// maintain) and another office's fund both answer 403 here, as does a fund that does not exist.
    /// The last is 403 rather than 404 on purpose — an office-scoped caller must not be able to probe
    /// which ids exist outside their office.
    /// </summary>
    private async Task<HttpResponseData?> DenyForeignFundAsync(
        HttpRequestData req, User caller, int fundId, CancellationToken ct)
    {
        ServiceResult<FundingSourceDto> existing = await _funding.GetByIdAsync(fundId, ct);
        if (!existing.IsSuccess || existing.Value!.OfficeId is null
            || existing.Value.OfficeId != caller.OfficeId)
            return req.CreateResponse(HttpStatusCode.Forbidden);
        return null;
    }

    // ── GET /api/config/funding-sources?search=&active=true|false|all&officeId= ──
    // Any authenticated user may read — funding sources are reference data used in WFP/AIP pickers.
    // The rows they get back are scoped: see VisibleOfficeIdAsync.
    [Function("FundingSourcesList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/funding-sources")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null || caller is null) return denied!;

        IReadOnlyList<FundingSourceDto> data = await _funding.GetAllAsync(
            req.Query["search"], ActiveFilterParser.Parse(req.Query["active"]),
            await VisibleOfficeIdAsync(caller, req, ct), ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK, ApiResponse<IReadOnlyList<FundingSourceDto>>.Ok(data), ct);
    }

    // ── GET /api/config/funding-sources/csv ──
    // Config managers only (PPDO-109) — a multi-office export, and its import counterpart below.
    [Function("FundingSourcesCsvExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/funding-sources/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await _funding.ExportCsvAsync(ct);
        return await ConfigHttp.CsvFileAsync(req, csv, "funding-sources.csv", ct);
    }

    // ── GET /api/config/funding-sources/{id} ──
    [Function("FundingSourcesGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/funding-sources/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageFundingSources, ct);
        if (denied is not null || caller is null) return denied!;

        // ⚠️ A shared fund is readable here even for an office-scoped caller — the config page loads
        // one to show it, and the list already hands them every shared row. Only the WRITES below
        // refuse a shared row.
        if (await IsOfficeScopedAsync(caller, ct))
        {
            ServiceResult<FundingSourceDto> existing = await _funding.GetByIdAsync(id, ct);
            if (!existing.IsSuccess
                || (existing.Value!.OfficeId is not null && existing.Value.OfficeId != caller.OfficeId))
                return req.CreateResponse(HttpStatusCode.Forbidden);
            return await ConfigHttp.FromResultAsync(req, existing, ct);
        }

        return await ConfigHttp.FromResultAsync(req, await _funding.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/funding-sources ──
    [Function("FundingSourcesCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/funding-sources")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageFundingSources, ct);
        if (denied is not null || caller is null) return denied!;

        UpsertFundingSourceDto? body = await ConfigHttp.ReadBodyAsync<UpsertFundingSourceDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<FundingSourceDto>.Fail("Request body is missing or malformed."), ct);

        if (await IsOfficeScopedAsync(caller, ct))
        {
            // No office of their own to create into — an unassigned user, which DECISION F says
            // sees nothing rather than everything.
            if (caller.OfficeId is null) return req.CreateResponse(HttpStatusCode.Forbidden);

            // Stamped from the caller, overwriting whatever the body said. An office-scoped caller
            // cannot create a shared fund (null) or one belonging to someone else.
            body = body with { OfficeId = caller.OfficeId };
        }

        return await ConfigHttp.FromResultAsync(req, await _funding.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/funding-sources/csv  (upsert) ──
    // Config managers only (PPDO-109) — keyed by code across every office's funds.
    [Function("FundingSourcesCsvImport")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/funding-sources/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        string csv = await ConfigHttp.ReadTextAsync(req);
        ServiceResult<CsvImportResult> result = await _funding.ImportCsvAsync(csv, ct);
        string? message = result.IsSuccess
            ? $"{result.Value!.New} added, {result.Value.Updated} updated, {result.Value.Skipped} skipped."
            : null;
        return await ConfigHttp.FromResultAsync(req, result, ct, message: message);
    }

    // ── PUT /api/config/funding-sources/{id} ──
    [Function("FundingSourcesUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/funding-sources/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageFundingSources, ct);
        if (denied is not null || caller is null) return denied!;

        UpsertFundingSourceDto? body = await ConfigHttp.ReadBodyAsync<UpsertFundingSourceDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<FundingSourceDto>.Fail("Request body is missing or malformed."), ct);

        if (await IsOfficeScopedAsync(caller, ct)
            && await DenyForeignFundAsync(req, caller, id, ct) is { } foreign)
            return foreign;

        return await ConfigHttp.FromResultAsync(req, await _funding.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/funding-sources/{id}  (soft delete) ──
    [Function("FundingSourcesDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/funding-sources/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageFundingSources, ct);
        if (denied is not null || caller is null) return denied!;

        bool officeScoped = await IsOfficeScopedAsync(caller, ct);
        if (officeScoped && await DenyForeignFundAsync(req, caller, id, ct) is { } foreign)
            return foreign;

        // ⚠️ The usage guard is passed ONLY for an office-scoped caller. A config manager keeps the
        // unconditional soft delete they have always had, because that is what soft delete is for:
        // retiring a fund from the pickers while decades of records keep resolving through it.
        // Applying the guard to them would make every fund that was ever used undeletable, which is
        // all of them. See IFundingSourceService.DeleteAsync.
        return await ConfigHttp.FromResultAsync(req, await _funding.DeleteAsync(id, officeScoped, ct), ct);
    }
}
