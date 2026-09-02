using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IEsreCodeRepository"/> (RAL-260).</summary>
public sealed class EsreCodeRepository : Repository<EsreCode>, IEsreCodeRepository
{
    public EsreCodeRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<int> CountAsync(bool? isActive, string? search, CancellationToken ct = default)
    {
        IQueryable<EsreCode> q = _context.Set<EsreCode>();

        if (isActive is bool active) q = q.Where(t => t.IsActive == active);

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            q = q.Where(t => t.Code.Contains(s) || t.Name.Contains(s));
        }

        return await q.CountAsync(ct);
    }
}
