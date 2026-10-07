using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="FundingSource"/> (PPDO-188 / O9). <c>FundingSource</c> has an int PK, so the
/// base <see cref="IRepository{T}.GetByIdAsync"/> (Guid-keyed) can't resolve one — callers used to
/// load the whole table and <c>FirstOrDefault</c> it in memory.
/// </summary>
public interface IFundingSourceRepository : IRepository<FundingSource>
{
    /// <summary>Returns the funding source whose integer PK equals <paramref name="id"/>, or null. Tracked.</summary>
    Task<FundingSource?> GetByIntIdAsync(int id, CancellationToken ct = default);
}
