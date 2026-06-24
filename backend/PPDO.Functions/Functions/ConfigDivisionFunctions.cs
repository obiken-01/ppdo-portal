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
/// Division config endpoints (<c>/api/config/divisions</c>) — minimal in RAL-97 (GET list only,
/// to drive the User Management division dropdown). Full CRUD + CSV upsert/export arrives in RAL-98.
///
/// The list allows CanManageConfig OR CanManageUsers (mirrors the offices dropdown). Responses use
/// the <c>{ data, error, message }</c> envelope.
/// </summary>
public sealed class ConfigDivisionFunctions
{
    private readonly IDivisionService   _divisions;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    public ConfigDivisionFunctions(IDivisionService divisions, IJwtMiddleware jwt, IPermissionService permissions)
    {
        _divisions   = divisions;
        _jwt         = jwt;
        _permissions = permissions;
    }

    private async Task<bool> CanReadDivisions(User u)
        => await _permissions.CanManageConfigAsync(u) || await _permissions.CanManageUsersAsync(u);

    // ── GET /api/config/divisions?active=true&officeId=  (also the user-form dropdown) ──
    [Function("DivisionsList")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/divisions")] HttpRequestData req,
        CancellationToken ct)
    {
        (User? caller, HttpResponseData? denied) = await ConfigHttp.AuthorizeAsync(req, _jwt, CanReadDivisions, ct);
        if (denied is not null) return denied;

        bool? activeOnly = string.Equals(req.Query["active"], "true", StringComparison.OrdinalIgnoreCase)
            ? true
            : null;
        int? officeId = int.TryParse(req.Query["officeId"], out int oid) ? oid : null;

        IReadOnlyList<DivisionDto> data = await _divisions.GetAllAsync(activeOnly, officeId, ct);

        return await ConfigHttp.EnvelopeAsync(req, HttpStatusCode.OK,
            ApiResponse<IReadOnlyList<DivisionDto>>.Ok(data), ct);
    }
}
