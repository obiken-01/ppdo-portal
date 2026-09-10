namespace PPDO.Application.DTOs.Auth;

/// <summary>
/// Response body for <c>POST /api/auth/forgot-password</c>. Always 200 with a question to
/// show — never a 404 for an unknown username. An unknown username or an account that hasn't
/// set a recovery question yet both fall back to the same catalog default, so this endpoint
/// cannot be used to enumerate accounts (RAL-265).
/// </summary>
public sealed record ForgotPasswordResponseDto(string QuestionText);
