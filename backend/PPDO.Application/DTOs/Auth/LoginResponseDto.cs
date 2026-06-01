namespace PPDO.Application.DTOs.Auth;

/// <summary>
/// Response body for <c>POST /api/auth/login</c> and <c>POST /api/auth/refresh</c>.
/// The client stores <see cref="AccessToken"/> in memory and <see cref="RefreshToken"/>
/// in an httpOnly cookie.
/// </summary>
public sealed record LoginResponseDto(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds);
