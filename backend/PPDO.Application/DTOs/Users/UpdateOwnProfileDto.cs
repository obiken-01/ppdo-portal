namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/me</c> — self-service profile update.
/// Only the five editable fields are exposed; Role, Division, OfficeId, permission
/// overrides, and IsActive are never touched by this endpoint.
/// </summary>
public sealed record UpdateOwnProfileDto(
    string  FullName,
    string  Username,
    string? Email,
    string? Position,
    string? ContactNo);
