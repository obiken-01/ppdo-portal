using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IAccountRepository"/> (PPDO-188 / O9).</summary>
public sealed class AccountRepository : Repository<Account>, IAccountRepository
{
    public AccountRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<Account?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<Account>().FirstOrDefaultAsync(x => x.Id == id, ct);
}
