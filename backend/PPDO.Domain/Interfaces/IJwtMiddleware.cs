using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Validates a JWT Bearer token and returns the authenticated <see cref="User"/> entity
/// (with Group navigation loaded) from the database.
///
/// Implemented in PPDO.Infrastructure/Services/JwtMiddleware.cs.
///
/// The caller is responsible for extracting the raw Authorization header value from the
/// HTTP request and passing it here. This keeps the interface framework-agnostic (no
/// dependency on HttpRequestData or IHttpContextAccessor).
///
/// Usage pattern in Azure Function handlers (from CLAUDE.md):
/// <code>
/// string? authHeader = req.Headers.TryGetValues("Authorization", out var vals)
///     ? vals.FirstOrDefault() : null;
/// var user = await _jwt.ValidateAsync(authHeader, cancellationToken);
/// if (user == null)
///     return req.CreateResponse(HttpStatusCode.Unauthorized);
/// </code>
/// </summary>
public interface IJwtMiddleware
{
    /// <summary>
    /// Validates the JWT in the supplied Authorization header value (e.g. "Bearer eyJ...").
    /// Returns null on any failure — null/missing header, wrong prefix, expired token,
    /// invalid signature, user not found in the database, or deactivated account.
    /// Never throws.
    /// </summary>
    /// <param name="authorizationHeader">
    /// The raw value of the Authorization header (e.g. "Bearer eyJ...").
    /// Pass null or empty to get an immediate null return.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<User?> ValidateAsync(string? authorizationHeader, CancellationToken cancellationToken = default);
}
