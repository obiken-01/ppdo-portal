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

    /// <summary>Returns the row whose integer PK equals <paramref name="id"/>, or null. Tracked (PPDO-188 / O9).</summary>
    Task<ClimateChangeTypology?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// True when an ACTIVE row carries exactly this code — the AIP activity save's check that a
    /// typology code is one Config offers (PPDO-291). An <c>AnyAsync</c> in SQL, never a list load.
    /// </summary>
    Task<bool> ActiveCodeExistsAsync(string code, CancellationToken ct = default);
}
