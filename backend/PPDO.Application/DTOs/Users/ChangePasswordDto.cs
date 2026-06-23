namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/me/password</c> — self-service password change.
/// </summary>
public sealed record ChangePasswordDto(
    string CurrentPassword,
    string NewPassword,
    string ConfirmPassword);
