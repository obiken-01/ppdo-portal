namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>POST /api/users</c>.
/// Role is a string name matching the enum (case-insensitive).
/// Division is a configurable division id (v1.2 — RAL-97); it carries the user's
/// scope + feature flags. Required for Staff; null for SuperAdmin/Admin.
///
/// <paramref name="OfficeId"/> creates a non-PPDO office user — its division must belong
/// to that office.
/// </summary>
public sealed record CreateUserDto(
    string FullName,
    string Username,
    string? Email,
    /// <summary>"SuperAdmin" | "Admin" | "Staff"</summary>
    string Role,
    /// <summary>Division id (divisions.id) — required for Staff, null for SuperAdmin/Admin.</summary>
    int? DivisionId,
    string? Position,
    string? ContactNo,
    /// <summary>Provincial office id (offices.id) — set for a non-PPDO office user.</summary>
    int? OfficeId = null);
