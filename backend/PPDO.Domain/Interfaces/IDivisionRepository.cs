using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="Division"/> (PPDO-188 / O9). <c>Division</c> has an int PK, so the
/// base <see cref="IRepository{T}.GetByIdAsync"/> (Guid-keyed) can't resolve one — callers used to
/// load the whole table and <c>FirstOrDefault</c> it in memory.
/// </summary>
public interface IDivisionRepository : IRepository<Division>
{
    /// <summary>Returns the division whose integer PK equals <paramref name="id"/>, or null. Tracked.</summary>
    Task<Division?> GetByIntIdAsync(int id, CancellationToken ct = default);
}
