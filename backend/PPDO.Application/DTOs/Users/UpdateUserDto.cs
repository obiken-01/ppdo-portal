namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/{id}</c>.
/// Only supplied (non-null) fields are applied — omit a field to leave it unchanged.
/// </summary>
public sealed record UpdateUserDto(
    string? FullName,
    string? Position,
    string? ContactNo);
