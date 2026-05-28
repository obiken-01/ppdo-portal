namespace PPDO.Application.DTOs.Auth;

/// <summary>Request body for <c>POST /api/auth/login</c>.</summary>
public sealed record LoginRequestDto(string Email, string Password);
