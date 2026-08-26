namespace PPDO.Application.DTOs.Auth;

/// <summary>
/// Success body for <c>POST /api/auth/verify-recovery</c>. Shown once — never stored or
/// logged in plaintext, same rule as <c>UserCredentialResponseDto.TemporaryPassword</c> (RAL-254).
/// </summary>
public sealed record VerifyRecoveryResponseDto(string TemporaryPassword);
