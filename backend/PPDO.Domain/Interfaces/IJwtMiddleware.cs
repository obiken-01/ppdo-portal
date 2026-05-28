using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Validates the JWT Bearer token in the current HTTP request and returns
/// the authenticated <see cref="User"/> entity (with Group navigation loaded).
///
/// Implemented in PPDO.Infrastructure/Services/JwtMiddleware.cs.
/// Reads the Authorization header via IHttpContextAccessor — no request parameter needed.
/// Also populates HttpContext.User with the validated ClaimsPrincipal so that
/// ICurrentUserService works inside Application services for the same request.
///
/// Usage pattern in Azure Function handlers (from CLAUDE.md):
/// <code>
/// var user = await _jwt.ValidateAsync();
/// if (user == null)
///     return req.CreateResponse(HttpStatusCode.Unauthorized);
/// </code>
/// </summary>
public interface IJwtMiddleware
{
    /// <summary>
    /// Validates the JWT in the Authorization: Bearer header of the current request.
    /// Returns null on any failure — missing header, expired token, invalid signature,
    /// user not found in the database, or deactivated account (<see cref="User.IsActive"/> = false).
    /// Never throws.
    /// </summary>
    Task<User?> ValidateAsync(CancellationToken cancellationToken = default);
}
