using PPDO.Domain.Enums;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Provides identity information for the currently authenticated user.
/// Implemented in PPDO.Infrastructure/Services/CurrentUserService.cs by reading
/// JWT claims from the incoming HTTP request context.
///
/// Use this in Application services to obtain the caller's identity without
/// taking a hard dependency on HttpContext or the JWT library.
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// The authenticated user's ID (the "sub" JWT claim).
    /// Null when the request is unauthenticated.
    /// </summary>
    Guid? UserId { get; }

    /// <summary>
    /// The authenticated user's email address.
    /// Null when unauthenticated.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// The authenticated user's role.
    /// Null when unauthenticated.
    /// </summary>
    UserRole? Role { get; }

    /// <summary>
    /// The authenticated user's division id (the "div" JWT claim).
    /// Null when unauthenticated or for SuperAdmin/Admin (no division).
    /// </summary>
    int? DivisionId { get; }

    /// <summary>True when a valid JWT has been presented and validated for this request.</summary>
    bool IsAuthenticated { get; }
}
