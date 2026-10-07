using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IDivisionRepository"/> (PPDO-188 / O9).</summary>
public sealed class DivisionRepository : Repository<Division>, IDivisionRepository
{
    public DivisionRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<Division?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<Division>().FirstOrDefaultAsync(x => x.Id == id, ct);
}
