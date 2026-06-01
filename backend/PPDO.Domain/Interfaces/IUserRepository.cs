using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Auth-specific user queries used by <c>AuthService</c>.
/// All methods load the <see cref="User.Group"/> navigation (depth 1) so that
/// <see cref="IPermissionService"/> can resolve effective permissions without a second query.
///
/// Extends <see cref="IRepository{User}"/> — inherited methods (GetByIdAsync, UpdateAsync,
/// SaveChangesAsync, etc.) are also available.
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// Returns the active user whose <see cref="User.Email"/> matches (case-insensitive),
    /// with <see cref="User.Group"/> included. Returns null if no match or the user
    /// is inactive.
    /// </summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user whose <see cref="User.RefreshToken"/> matches exactly,
    /// with <see cref="User.Group"/> included. Returns null if no match.
    /// The caller is responsible for checking <see cref="User.RefreshTokenExpiry"/>.
    /// </summary>
    Task<User?> FindByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);
}
