namespace PPDO.Application.DTOs.Auth;

/// <summary>
/// Error body for a failed <c>POST /api/auth/refresh</c> (RAL-198). Lets the frontend
/// explain the logout instead of a bare 401. <see cref="Reason"/> is one of
/// <c>token_superseded</c> or <c>token_expired</c>.
/// </summary>
public sealed record RefreshErrorDto(string Reason);
