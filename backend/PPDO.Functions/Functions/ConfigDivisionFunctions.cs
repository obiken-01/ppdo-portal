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
/// Division config endpoints (<c>/api/config/divisions</c>) — RAL-97 (GET list) + RAL-98 (full CRUD + CSV).
///
/// The GET list allows CanManageConfig OR CanManageUsers so the User Management division dropdown
/// keeps working. All writes (POST/PUT/DELETE/CSV) require CanManageConfig. Responses use the
/// <c>{ data, error, message }</c> envelope. Soft delete only.
///
/// ↩️ <b>PPDO-108 adds a second, narrower writer:</b> a department head holding
/// <c>CanManageOfficeSetup</c> manages their OWN office's divisions. Three rules make that safe,
/// and all three are enforced HERE rather than in the UI:
/// <list type="number">
///   <item><description>every read is clamped and every write refused unless the division belongs
///   to <c>caller.OfficeId</c>;</description></item>
///   <item><description>the seven feature switches in the payload are <b>dropped</b> — a created
///   division gets all seven off, an updated one keeps what it had (D8/D9). Hiding the controls in
///   the form is not enforcement: "we own the site, we can't let them set permissions of our
///   features" (Ralph, 2026-09-16);</description></item>
///   <item><description>the CSV routes stay <c>CanManageConfig</c>-only — a bulk upsert carries an
///   office column per row, so an office-scoped caller has no safe reading of it.</description></item>
/// </list>
/// </summary>
public sealed class ConfigDivisionFunctions
{
    private readonly IDivisionService   _divisions;
    private readonly IOfficeService     _offices;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public ConfigDivisionFunctions(
        IDivisionService   divisions,
        IOfficeService     offices,
        IJwtMiddleware     jwt,
        IPermissionService permissions)
    {
        _divisions   = divisions;
        _offices     = offices;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private Task<bool> CanManageConfig(User u)  => _permissions.CanManageConfigAsync(u);

    /// <summary>Config managers, plus a department head for their own office (PPDO-108).</summary>
    private async Task<bool> CanManageDivisions(User u)
        => await _permissions.CanManageConfigAsync(u)
        || await _permissions.CanManageOfficeSetupAsync(u);

    /// <summary>
    /// True for a caller who got in on <c>CanManageOfficeSetup</c> alone — the one whose reads are
    /// clamped, whose writes are office-checked and whose permission switches are dropped.
    ///
    /// ⚠️ Asked as "not a config manager" rather than "holds the setup grant": someone holding both
    /// is a config manager and must keep the full page, otherwise granting a PPDO admin the new flag
    /// would quietly take away every other office's divisions.
    /// </summary>
    private async Task<bool> IsOfficeScopedAsync(User u, CancellationToken ct)
        => !await _permissions.CanManageConfigAsync(u, ct);

    /// <summary>
    /// 403 unless <paramref name="divisionId"/> belongs to the caller's own office. Only called for
    /// an office-scoped caller; a config manager never reaches it.
    /// </summary>
    private async Task<HttpResponseData?> DenyForeignDivisionAsync(
        HttpRequestData req, User caller, int divisionId, CancellationToken ct)
    {
        ServiceResult<DivisionDto> existing = await _divisions.GetByIdAsync(divisionId, ct);
        // ⚠️ A division that does not exist answers 403 here too, not 404: an office-scoped caller
        // must not be able to probe which ids exist outside their office.
        if (!existing.IsSuccess || existing.Value!.OfficeId != caller.OfficeId)
            return req.CreateResponse(HttpStatusCode.Forbidden);
        return null;
    }

    /// <summary>
    /// The payload as an office-scoped caller may actually write it: pinned to their own office,
    /// with the seven feature switches taken from <paramref name="current"/> — the stored division
    /// on an update, or all-false on a create (D8/D9).
    ///
    /// ⚠️ Dropped, never rejected. A 400 would make an older client that still sends the fields
    /// unusable, and the fields are not the caller's to set either way.
    /// </summary>
    private static UpsertDivisionDto WithoutPermissionFields(
        UpsertDivisionDto body, User caller, DivisionDto? current) => body with
        {
            OfficeId               = caller.OfficeId ?? 0,
            CanAccessInventory     = current?.CanAccessInventory     ?? false,
            CanAccessReports       = current?.CanAccessReports       ?? false,
            CanManageUsers         = current?.CanManageUsers         ?? false,
            CanManageResourceLinks = current?.CanManageResourceLinks ?? false,
            CanAccessBudgetPlanning= current?.CanAccessBudgetPlanning?? false,
            CanUploadAip           = current?.CanUploadAip           ?? false,
            CanManageConfig        = current?.CanManageConfig        ?? false,
        };

    // Any authenticated user may read divisions — divisions are reference data used in user-form and allocation dropdowns.

    // ── GET /api/config/divisions?active=true&officeId=  (also the user-form dropdown) ──
    [Function("DivisionsList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, ConfigHttp.Authenticated, ct);
        if (denied is not null) return denied;

        bool? activeOnly = (req.Query["active"] ?? "").Trim().ToLowerInvariant() switch
        {
            "true"  => true,
            "false" => false,
            _       => null,
        };
        int? officeId = int.TryParse(req.Query["officeId"], out int oid) ? oid : null;

        // PPDO-108 — an office-scoped caller reads their own office whatever they asked for.
        // Clamped rather than refused: the same reasoning as ConfigHttp.ClampOfficeId, and this
        // route is also the user-form dropdown, which asks for no office at all.
        if (caller is not null && await _permissions.CanManageOfficeSetupAsync(caller, ct)
            && await IsOfficeScopedAsync(caller, ct))
            officeId = caller.OfficeId;

        IReadOnlyList<DivisionDto> data = await _divisions.GetAllAsync(activeOnly, officeId, ct);
        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<DivisionDto>>.Ok(data), ct);
    }

    // ── GET /api/config/divisions/csv ──
    [Function("DivisionsCsvExport")]
    public async Task<HttpResponseData> Export(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/divisions/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        IReadOnlyList<OfficeDto> officeDtos = await _offices.GetAllAsync(search: null, ActiveFilter.All, ct);
        IReadOnlyList<Office> offices = officeDtos.Select(o => new Office
        {
            Id = o.Id, OfficeCode = o.OfficeCode, OfficeName = o.OfficeName,
            IsActive = o.IsActive, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }).ToList();

        string csv = await _divisions.ExportCsvAsync(offices, ct);
        return await ConfigHttp.CsvFileAsync(req, csv, "divisions.csv", ct);
    }

    // ── GET /api/config/divisions/{id} ──
    [Function("DivisionsGet")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/divisions/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageDivisions, ct);
        if (denied is not null || caller is null) return denied!;

        if (await IsOfficeScopedAsync(caller, ct)
            && await DenyForeignDivisionAsync(req, caller, id, ct) is { } foreign)
            return foreign;

        return await ConfigHttp.FromResultAsync(req, await _divisions.GetByIdAsync(id, ct), ct);
    }

    // ── POST /api/config/divisions ──
    [Function("DivisionsCreate")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageDivisions, ct);
        if (denied is not null || caller is null) return denied!;

        UpsertDivisionDto? body = await ConfigHttp.ReadBodyAsync<UpsertDivisionDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<DivisionDto>.Fail("Request body is missing or malformed."), ct);

        if (await IsOfficeScopedAsync(caller, ct))
        {
            // No office of their own to create into — an unassigned user, which DECISION F says
            // sees nothing rather than everything.
            if (caller.OfficeId is null) return req.CreateResponse(HttpStatusCode.Forbidden);
            body = WithoutPermissionFields(body, caller, current: null);
        }

        return await ConfigHttp.FromResultAsync(req, await _divisions.CreateAsync(body, ct), ct, HttpStatusCode.Created);
    }

    // ── POST /api/config/divisions/csv  (upsert) ──
    [Function("DivisionsCsvImport")]
    public async Task<HttpResponseData> Import(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "config/divisions/csv")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageConfig, ct);
        if (denied is not null) return denied;

        IReadOnlyList<OfficeDto> officeDtos = await _offices.GetAllAsync(search: null, ActiveFilter.All, ct);
        IReadOnlyList<Office> offices = officeDtos.Select(o => new Office
        {
            Id = o.Id, OfficeCode = o.OfficeCode, OfficeName = o.OfficeName,
            IsActive = o.IsActive, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        }).ToList();

        string csv = await ConfigHttp.ReadTextAsync(req);
        ServiceResult<CsvImportResult> result = await _divisions.ImportCsvAsync(csv, offices, ct);
        string? message = result.IsSuccess
            ? $"{result.Value!.New} added, {result.Value.Updated} updated, {result.Value.Skipped} skipped."
            : null;
        return await ConfigHttp.FromResultAsync(req, result, ct, message: message);
    }

    // ── PUT /api/config/divisions/{id} ──
    [Function("DivisionsUpdate")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "config/divisions/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageDivisions, ct);
        if (denied is not null || caller is null) return denied!;

        UpsertDivisionDto? body = await ConfigHttp.ReadBodyAsync<UpsertDivisionDto>(req, ct);
        if (body is null)
            return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.BadRequest,
                ApiResponse<DivisionDto>.Fail("Request body is missing or malformed."), ct);

        if (await IsOfficeScopedAsync(caller, ct))
        {
            ServiceResult<DivisionDto> existing = await _divisions.GetByIdAsync(id, ct);
            if (!existing.IsSuccess || existing.Value!.OfficeId != caller.OfficeId)
                return req.CreateResponse(HttpStatusCode.Forbidden);

            // ⚠️ The switches come from the STORED row, not the request — so an edit can never
            // raise one, and cannot silently clear one an admin set either.
            body = WithoutPermissionFields(body, caller, existing.Value);
        }

        return await ConfigHttp.FromResultAsync(req, await _divisions.UpdateAsync(id, body, ct), ct);
    }

    // ── DELETE /api/config/divisions/{id}  (soft delete) ──
    [Function("DivisionsDelete")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "config/divisions/{id:int}")] HttpRequestData req,
        int id, CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanManageDivisions, ct);
        if (denied is not null || caller is null) return denied!;

        if (await IsOfficeScopedAsync(caller, ct)
            && await DenyForeignDivisionAsync(req, caller, id, ct) is { } foreign)
            return foreign;

        return await ConfigHttp.FromResultAsync(req, await _divisions.DeleteAsync(id, ct), ct);
    }
}
