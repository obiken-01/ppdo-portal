namespace PPDO.Domain.Common;

/// <summary>
/// JWT claim name constants used by both JwtMiddleware (token validation, PPDO.Infrastructure)
/// and AuthService (token creation, PPDO.Application).
///
/// Centralised here in Domain so both layers can reference the same constants
/// without introducing a cross-layer dependency.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>Subject — the authenticated user's primary key (Guid, stored as string).</summary>
    public const string Sub = "sub";

    /// <summary>The user's email address.</summary>
    public const string Email = "email";

    /// <summary>
    /// The user's <see cref="PPDO.Domain.Enums.UserRole"/> stored as its integer value.
    /// e.g. SuperAdmin = "0", Admin = "1", Staff = "2", Observer = "3".
    /// </summary>
    public const string Role = "role";

    /// <summary>
    /// The user's <see cref="PPDO.Domain.Enums.Division"/> stored as its integer value.
    /// e.g. Admin = "0", Planning = "1", RM = "2", MIS = "3", SPD = "4".
    /// </summary>
    public const string Division = "div";
}
