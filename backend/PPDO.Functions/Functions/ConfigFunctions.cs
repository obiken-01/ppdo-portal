using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for budget-planning configuration reads (<c>/api/config</c>).
///
/// RAL-81 ships only the offices read used by the User Management office dropdown.
/// Full config CRUD + CSV upload (Accounts, Offices, Funding Sources) is RAL-70.
///
/// Access: a valid JWT plus CanManageUsers (user form) or CanManageConfig (config pages).
/// </summary>
public sealed class ConfigFunctions
{
    private readonly IOfficeService     _offices;
    private readonly IJwtMiddleware     _jwt;
    private readonly IPermissionService _permissions;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public ConfigFunctions(
        IOfficeService     offices,
        IJwtMiddleware     jwt,
        IPermissionService permissions)
    {
        _offices     = offices;
        _jwt         = jwt;
        _permissions = permissions;
    }

    // ── GET /api/config/offices?active=true ────────────────────────────────────

    [Function("GetConfigOffices")]
    public async Task<HttpResponseData> GetOffices(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "config/offices")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        bool canManageUsers  = await _permissions.CanManageUsersAsync(caller, cancellationToken);
        bool canManageConfig = await _permissions.CanManageConfigAsync(caller, cancellationToken);
        if (!canManageUsers && !canManageConfig)
            return req.CreateResponse(HttpStatusCode.Forbidden);

        // Default to active-only; ?active=false returns all (including soft-deleted).
        bool activeOnly = !string.Equals(
            req.Query["active"], "false", StringComparison.OrdinalIgnoreCase);

        IReadOnlyList<OfficeDto> offices = await _offices.GetAllAsync(activeOnly, cancellationToken);

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(offices, _jsonOptions), cancellationToken);
        return response;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
}
