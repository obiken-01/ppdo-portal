namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>POST /api/users</c>.
/// Role and Division are string names matching the enum members (case-insensitive).
/// The PermissionGroup is auto-assigned from Role + Division (or office) — callers do
/// not supply GroupId.
///
/// v1.1: <paramref name="OfficeId"/> creates a non-PPDO office user. When set, Division
/// is ignored (office users have no division) and the "Office User Default" group is used.
/// </summary>
public sealed record CreateUserDto(
    string FullName,
    string Username,
    string? Email,
    /// <summary>"SuperAdmin" | "Admin" | "Staff" | "Observer"</summary>
    string Role,
    /// <summary>"Admin" | "Planning" | "RM" | "MIS" | "SPD" — optional when OfficeId is set.</summary>
    string? Division,
    string? Position,
    string? ContactNo,
    /// <summary>Provincial office id (offices.id) — set for a non-PPDO office user.</summary>
    int? OfficeId = null);
