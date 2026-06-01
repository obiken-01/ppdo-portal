namespace PPDO.Application.DTOs.Auth;

/// <summary>Response body for <c>GET /api/auth/me</c>.</summary>
public sealed class MeResponseDto
{
    public Guid UserId { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;

    /// <summary>Role name string, e.g. "SuperAdmin", "Admin", "Staff", "Observer".</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>Division name string, e.g. "Admin", "Planning", "RM", "MIS", "SPD".</summary>
    public string Division { get; init; } = string.Empty;

    public string? Position { get; init; }

    // -- Effective permission flags (resolved via PermissionService) ----------
    public bool CanAccessInventory { get; init; }
    public bool CanAccessReports { get; init; }
    public bool CanManageUsers { get; init; }
    public bool CanAccessProfile { get; init; }
    public bool CanManageResourceLinks { get; init; }
}
