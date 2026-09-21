using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for scoped, server-side <see cref="Office"/> reads (v1.4.5 — RAL-161).
/// <c>Office</c> has an int PK, so the base <see cref="IRepository{T}.GetByIdAsync"/> (Guid-keyed)
/// can't be used — before this interface existed, every caller resolved an office via
/// <c>GetAllAsync()</c> + an in-memory <c>FirstOrDefault</c>, an unfiltered full-table scan.
/// </summary>
public interface IOfficeRepository : IRepository<Office>
{
    /// <summary>Returns the office whose integer PK equals <paramref name="id"/>, or null.</summary>
    Task<Office?> GetByIdAsync(int id, CancellationToken ct = default);

    /// <summary>Returns the office whose OfficeCode equals <paramref name="code"/> (case-insensitive), or null.</summary>
    Task<Office?> GetByCodeAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Returns the office flagged <see cref="Office.IsHostOffice"/>, or null when none is
    /// (DECISION F, RAL-258). Replaces looking the host office up by the hardcoded code "PPDO" —
    /// the flag survives a rename, and a filtered unique index guarantees at most one match.
    /// </summary>
    Task<Office?> GetHostOfficeAsync(CancellationToken ct = default);
}
