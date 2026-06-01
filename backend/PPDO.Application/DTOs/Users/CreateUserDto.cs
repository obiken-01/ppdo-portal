namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>POST /api/users</c>.
/// Role and Division are string names matching the enum members (case-insensitive).
/// The PermissionGroup is auto-assigned from the division — callers do not supply GroupId.
/// </summary>
public sealed record CreateUserDto(
    string FullName,
    string Email,
    /// <summary>"SuperAdmin" | "Admin" | "Staff" | "Observer"</summary>
    string Role,
    /// <summary>"Admin" | "Planning" | "RM" | "MIS" | "SPD"</summary>
    string Division,
    string? Position,
    string? ContactNo);
