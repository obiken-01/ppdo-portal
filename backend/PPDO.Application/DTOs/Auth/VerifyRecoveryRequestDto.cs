namespace PPDO.Application.DTOs.Auth;

/// <summary>Request body for <c>POST /api/auth/verify-recovery</c>.</summary>
public sealed record VerifyRecoveryRequestDto(string Username, string Answer);
