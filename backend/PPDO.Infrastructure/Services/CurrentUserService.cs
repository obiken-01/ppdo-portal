using Microsoft.AspNetCore.Http;
using PPDO.Domain.Common;
using PPDO.Domain.Interfaces;
using UserRoleEnum = PPDO.Domain.Enums.UserRole;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Reads the current authenticated user's identity from <c>HttpContext.User</c> claims.
/// <c>HttpContext.User</c> is populated by <see cref="JwtMiddleware.ValidateAsync"/> after a
/// successful token validation, so this service only has meaningful values on requests that
/// have already passed through that validation step.
///
/// Inject into Application services to access caller identity without taking a hard
/// dependency on <see cref="HttpContext"/> or the JWT library.
///
/// Claim values are stored as integers by AuthService:
///   Role     → <see cref="PPDO.Domain.Enums.UserRole"/> cast to int
///   Division → division id (divisions.id)
/// </summary>
public sealed class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    /// <inheritdoc />
    public Guid? UserId
    {
        get
        {
            string? sub = _httpContextAccessor.HttpContext?
                .User.FindFirst(JwtClaimNames.Sub)?.Value;
            return Guid.TryParse(sub, out Guid id) ? id : null;
        }
    }

    /// <inheritdoc />
    public string? Email =>
        _httpContextAccessor.HttpContext?.User.FindFirst(JwtClaimNames.Email)?.Value;

    /// <inheritdoc />
    public UserRoleEnum? Role
    {
        get
        {
            string? roleClaim = _httpContextAccessor.HttpContext?
                .User.FindFirst(JwtClaimNames.Role)?.Value;
            return int.TryParse(roleClaim, out int roleInt)
                ? (UserRoleEnum)roleInt
                : null;
        }
    }

    /// <inheritdoc />
    public int? DivisionId
    {
        get
        {
            string? divClaim = _httpContextAccessor.HttpContext?
                .User.FindFirst(JwtClaimNames.Division)?.Value;
            return int.TryParse(divClaim, out int divInt) ? divInt : null;
        }
    }

    /// <inheritdoc />
    public bool IsAuthenticated =>
        _httpContextAccessor.HttpContext?.User.Identity?.IsAuthenticated ?? false;
}
