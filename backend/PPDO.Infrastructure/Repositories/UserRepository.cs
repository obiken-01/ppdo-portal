using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// Auth-specific user queries for <see cref="IUserRepository"/>.
///
/// All find methods Include <see cref="User.Division"/> and <see cref="User.Office"/> at depth 1:
/// <c>PermissionService</c> resolves feature flags off the division, and <c>OfficeScope</c> reads
/// cross-office authority off <see cref="Office.IsHostOffice"/> (DECISION F, RAL-258). Both must
/// be loaded or the caller silently degrades — a missing Office scopes a host-office user to their
/// own office instead of granting the bypass.
///
/// ⚠️ The Office join is on the login and token-refresh paths, so it runs on effectively every
/// authenticated request. It is depth 1 over a ~16-row table, which is the cost DECISION F
/// accepted in exchange for retiring the null discriminator.
/// </summary>
public sealed class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public Task<User?> FindByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Division)
            .Include(u => u.Office)
            .FirstOrDefaultAsync(
                // No ToLower() on either side — the DB collation is case-insensitive, and wrapping
                // the column in LOWER() makes the predicate non-SARGable so IX_Users_Username
                // cannot be seeked (RAL-204).
                u => u.Username == username && u.IsActive,
                cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Division)
            .Include(u => u.Office)
            .FirstOrDefaultAsync(
                // See FindByUsernameAsync — same non-SARGable LOWER() issue, IX_Users_Email.
                u => u.Email != null && u.Email == email && u.IsActive,
                cancellationToken);

    /// <inheritdoc />
    public Task<User?> FindByRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Division)
            .Include(u => u.Office)
            .FirstOrDefaultAsync(
                u => u.RefreshToken == refreshToken,
                cancellationToken);

    /// <inheritdoc />
    public Task<User?> GetByIdWithDivisionAsync(
        Guid id,
        CancellationToken cancellationToken = default)
        => _context.Users
            .Include(u => u.Division)
            .Include(u => u.Office)   // v1.1 — OfficeName for the user detail/list DTO
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<User>> GetAllWithDivisionAsync(
        CancellationToken cancellationToken = default)
        => await _context.Users
            .Include(u => u.Division)
            .Include(u => u.Office)   // v1.1 — OfficeName for the user list DTO
            .OrderBy(u => u.FullName)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default)
    {
        if (ids.Count == 0) return new Dictionary<Guid, string>();
        return await _context.Users
            .Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, cancellationToken);
    }
}
