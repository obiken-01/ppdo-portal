using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IPartnerApiRequestRepository"/> (v1.8.0 — PPDO-15).</summary>
public sealed class PartnerApiRequestRepository : Repository<PartnerApiRequest>, IPartnerApiRequestRepository
{
    public PartnerApiRequestRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<(IReadOnlyList<PartnerApiRequest> Items, int Total)> GetPageForKeyAsync(
        int keyId, int page, int pageSize, CancellationToken ct = default)
    {
        IQueryable<PartnerApiRequest> q = _context.Set<PartnerApiRequest>()
            .Where(r => r.KeyId == keyId);

        int total = await q.CountAsync(ct);

        List<PartnerApiRequest> items = await q
            .OrderByDescending(r => r.RequestedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }
}
