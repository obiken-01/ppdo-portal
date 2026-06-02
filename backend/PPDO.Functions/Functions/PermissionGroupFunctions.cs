using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.DTOs.Users;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for permission group reads (<c>/api/permission-groups</c>).
/// All endpoints require a valid JWT with CanManageUsers permission.
/// </summary>
public sealed class PermissionGroupFunctions
{
    private readonly IRepository<PermissionGroup> _groups;
    private readonly IJwtMiddleware               _jwt;
    private readonly IPermissionService           _permissions;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public PermissionGroupFunctions(
        IRepository<PermissionGroup> groups,
        IJwtMiddleware               jwt,
        IPermissionService           permissions)
    {
        _groups      = groups;
        _jwt         = jwt;
        _permissions = permissions;
    }

    // ── GET /api/permission-groups ─────────────────────────────────────────────

    [Function("GetPermissionGroups")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "permission-groups")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        IReadOnlyList<PermissionGroup> groups = await _groups.GetAllAsync(cancellationToken);

        IEnumerable<PermissionGroupResponseDto> dtos = groups
            .OrderBy(g => g.Name)
            .Select(g => new PermissionGroupResponseDto(
                g.Id,
                g.Name,
                g.Division?.ToString(),
                g.CanAccessInventory,
                g.CanAccessReports,
                g.CanManageUsers,
                g.CanManageResourceLinks));

        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(dtos, _jsonOptions), cancellationToken);
        return response;
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
}
