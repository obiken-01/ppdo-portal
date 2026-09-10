namespace PPDO.Application.DTOs.Auth;

/// <summary>Request body for <c>POST /api/auth/forgot-password</c>.</summary>
public sealed record ForgotPasswordRequestDto(string Username);
