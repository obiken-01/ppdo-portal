namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Returned by the two endpoints that issue a password — create user and admin reset.
/// <see cref="TemporaryPassword"/> is the only moment the plaintext exists outside the
/// admin's screen: it is never stored, never logged, and cannot be retrieved afterwards.
/// If the admin loses it, the password has to be reset a second time.
/// </summary>
public sealed class UserCredentialResponseDto
{
    /// <summary>The created or updated user record.</summary>
    public required UserResponseDto User { get; init; }

    /// <summary>Plaintext password, shown once. Never persisted.</summary>
    public required string TemporaryPassword { get; init; }
}
