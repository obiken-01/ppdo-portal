namespace PPDO.Application.DTOs.Users;

/// <summary>
/// User record returned by all <c>/api/users</c> endpoints.
/// Role and Division are serialised as their enum name strings (e.g. "Staff", "Admin").
/// </summary>
public sealed class UserResponseDto
{
    public Guid Id { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;

    /// <summary>"SuperAdmin" | "Admin" | "Staff" | "Observer"</summary>
    public string Role { get; init; } = string.Empty;

    /// <summary>"Admin" | "Planning" | "RM" | "MIS" | "SPD"</summary>
    public string? Division { get; init; }

    // -- Office (v1.1) — set for non-PPDO office users -------------------------
    public int? OfficeId { get; init; }
    public string? OfficeName { get; init; }

    public string? Position { get; init; }
    public string? ContactNo { get; init; }
    public bool IsActive { get; init; }

    // -- Permission group -------------------------------------------------------
    public Guid? GroupId { get; init; }
    public string? GroupName { get; init; }

    // -- Individual permission overrides (null = inherit from group) -----------
    public bool? OverrideCanAccessInventory { get; init; }
    public bool? OverrideCanAccessReports { get; init; }
    public bool? OverrideCanManageUsers { get; init; }
    public bool? OverrideCanManageResourceLinks { get; init; }
    public bool? OverrideCanAccessBudgetPlanning { get; init; }
    public bool? OverrideCanUploadAip { get; init; }
    public bool? OverrideCanManageConfig { get; init; }

    // -- Audit -----------------------------------------------------------------
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}
