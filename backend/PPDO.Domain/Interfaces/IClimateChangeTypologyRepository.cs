using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="ClimateChangeTypology"/> (RAL-260).
/// Adds a SQL-side count on top of the generic <see cref="IRepository{T}"/> so the Config
/// dashboard tile never downloads the list to measure it — see the tile's remarks and RAL-232.
/// </summary>
public interface IClimateChangeTypologyRepository : IRepository<ClimateChangeTypology>
{
    /// <summary>
    /// Counts rows matching the same filters the list endpoint applies, in SQL.
    /// <paramref name="isActive"/> null means "no status filter".
    /// </summary>
    Task<int> CountAsync(bool? isActive, string? search, CancellationToken ct = default);
}
