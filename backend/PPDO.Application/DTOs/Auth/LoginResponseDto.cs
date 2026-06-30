namespace PPDO.Application.DTOs.Auth;

/// <summary>
/// Response body for <c>POST /api/auth/login</c> and <c>POST /api/auth/refresh</c>.
/// The client stores <see cref="AccessToken"/> in memory only. The refresh token is
/// never returned in the body — it is set as an httpOnly cookie (RAL-58).
/// </summary>
public sealed record LoginResponseDto(
    string AccessToken,
    int ExpiresInSeconds);
