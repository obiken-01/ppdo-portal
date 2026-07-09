using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped, server-side procurement preset reads (v1.4 WFP Rework —
/// RAL-119). Pushes the account_id filter to SQL rather than loading every preset in memory
/// (unlike the tiny funding_sources table, presets can grow per account over time).
/// </summary>
public interface IProcurementPresetRepository : IRepository<ProcurementPreset>
{
    /// <summary>
    /// Returns the preset whose integer PK equals <paramref name="id"/>, with Items and
    /// CreatedBy loaded, or null. Needed because the base
    /// <see cref="IRepository{T}.GetByIdAsync"/> uses a Guid key.
    /// </summary>
    Task<ProcurementPreset?> GetByIntIdAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Presets WHERE account_id = <paramref name="accountId"/>, with Items and CreatedBy
    /// loaded, ordered by Name.
    /// </summary>
    Task<IReadOnlyList<ProcurementPreset>> GetByAccountIdAsync(int accountId, CancellationToken ct = default);
}
