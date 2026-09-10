using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="EsreCode"/> (RAL-260).
/// Adds a SQL-side count on top of the generic <see cref="IRepository{T}"/>. The table holds four
/// rows, so the payload saved is trivial — the point is that the tile added here is the tile the
/// next config page copies, and RAL-232 is what that pattern costs at scale.
/// </summary>
public interface IEsreCodeRepository : IRepository<EsreCode>
{
    /// <summary>
    /// Counts rows matching the same filters the list endpoint applies, in SQL.
    /// <paramref name="isActive"/> null means "no status filter".
    /// </summary>
    Task<int> CountAsync(bool? isActive, string? search, CancellationToken ct = default);
}
