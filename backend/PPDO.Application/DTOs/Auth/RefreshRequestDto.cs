namespace PPDO.Application.DTOs.Auth;

/// <summary>Request body for <c>POST /api/auth/refresh</c>.</summary>
public sealed record RefreshRequestDto(string RefreshToken);
