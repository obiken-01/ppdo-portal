using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// Auth-specific user queries for <see cref="IUserRepository"/>.
/// All find methods Include <see cref="User.Group"/> at depth 1 so that
/// <c>PermissionService</c> can resolve flags without a second query.
/// </summary>
public sealed class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Group)
            .FirstOrDefaultAsync(
                u => u.Email.ToLower() == email.ToLower() && u.IsActive,
                cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Group)
            .FirstOrDefaultAsync(
                u => u.RefreshToken == refreshToken,
                cancellationToken);

    /// <inheritdoc />
    public Task<User?> GetByIdWithGroupAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Group)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetAllWithGroupAsync(
        CancellationToken cancellationToken = default)
        => await _context.Users
            .Include(u => u.Group)
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);
}
