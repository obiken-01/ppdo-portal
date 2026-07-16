using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IOfficeRepository"/> (v1.4.5 — RAL-161).</summary>
public sealed class OfficeRepository : Repository<Office>, IOfficeRepository
{
    public OfficeRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<Office?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<Office>()
            .FirstOrDefaultAsync(o => o.Id == id, ct);

    /// <inheritdoc />
    public async Task<Office?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _context.Set<Office>()
            .FirstOrDefaultAsync(o => o.OfficeCode == code, ct);
}
