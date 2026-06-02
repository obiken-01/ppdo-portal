namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Response DTO for <c>GET /api/permission-groups</c>.
/// </summary>
public sealed record PermissionGroupResponseDto(
    Guid    Id,
    string  Name,
    string? Division,
    bool    CanAccessInventory,
    bool    CanAccessReports,
    bool    CanManageUsers,
    bool    CanManageResourceLinks);
