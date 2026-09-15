using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IPartnerApiKeyRepository"/> (v1.8.0 — PPDO-15).</summary>
public sealed class PartnerApiKeyRepository : Repository<PartnerApiKey>, IPartnerApiKeyRepository
{
    public PartnerApiKeyRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<PartnerApiKey?> GetByIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<PartnerApiKey>()
            .Include(k => k.Offices)                // depth 1
                .ThenInclude(o => o.Office)          // depth 2
            .FirstOrDefaultAsync(k => k.Id == id, ct);

    /// <inheritdoc />
    public async Task<PartnerApiKey?> GetByPrefixAsync(string keyPrefix, CancellationToken ct = default)
        => await _context.Set<PartnerApiKey>()
            .Include(k => k.Offices)                // depth 1
                .ThenInclude(o => o.Office)          // depth 2
            .FirstOrDefaultAsync(k => k.KeyPrefix == keyPrefix, ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PartnerApiKey>> GetAllWithOfficesAsync(CancellationToken ct = default)
        => await _context.Set<PartnerApiKey>()
            .Include(k => k.Offices)                // depth 1
                .ThenInclude(o => o.Office)          // depth 2
            .Include(k => k.CreatedBy)               // depth 1 — sibling
            .Include(k => k.RevokedBy)               // depth 1 — sibling
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(ct);
}
