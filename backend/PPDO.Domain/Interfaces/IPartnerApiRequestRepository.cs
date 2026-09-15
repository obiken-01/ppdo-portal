using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="PartnerApiRequest"/> (v1.8.0 — PPDO-15/PPDO-13). No
/// unique-key lookup — every read is either a bulk insert-per-call (via the base
/// <see cref="IRepository{T}.AddAsync"/>) or the paged usage list below. Table is expected to grow
/// steadily (build spec §2: "revisit retention at 100k rows"), so the usage list is a
/// <c>Skip</c>/<c>Take</c> SQL query, never <c>GetAllAsync()</c> filtered in memory.
/// </summary>
public interface IPartnerApiRequestRepository : IRepository<PartnerApiRequest>
{
    /// <summary>
    /// One page of <paramref name="keyId"/>'s call history, newest first, plus the total row
    /// count for pagination — the "Usage" modal on Configuration → API Access (build spec §6.1).
    /// </summary>
    Task<(IReadOnlyList<PartnerApiRequest> Items, int Total)> GetPageForKeyAsync(
        int keyId, int page, int pageSize, CancellationToken ct = default);
}
