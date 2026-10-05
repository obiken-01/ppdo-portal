using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IFundingSourceRepository"/> (PPDO-188 / O9).</summary>
public sealed class FundingSourceRepository : Repository<FundingSource>, IFundingSourceRepository
{
    public FundingSourceRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<FundingSource?> GetByIntIdAsync(int id, CancellationToken ct = default)
        => await _context.Set<FundingSource>().FirstOrDefaultAsync(x => x.Id == id, ct);
}
