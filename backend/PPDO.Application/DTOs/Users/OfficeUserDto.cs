namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Slim user record for the office-scoped division-assignment screen (PPDO-135) — a department
/// head managing their own office's Staff needs to see who exists and which division they carry,
/// nothing else. Deliberately narrower than <c>UserResponseDto</c>: no email, no permission
/// overrides, no audit timestamps — none of that is this screen's business, and CLAUDE.md's
/// slim-DTO rule for list/grid endpoints applies to an internal screen as much as a public one.
/// </summary>
public sealed record OfficeUserDto(
    Guid Id,
    string FullName,
    string Username,
    string? Position,
    bool IsActive,
    int? DivisionId,
    string? Division);
