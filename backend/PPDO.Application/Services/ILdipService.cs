using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// LDIP (Local Development Investment Program) CRUD + status lifecycle (RAL-64, RAL-61).
/// Status workflow: Draft → Final (finalize) → Draft (unlock, admin only) → Archived.
/// v1.3: documents are office-scoped and carry a sector-grouped program hierarchy;
/// AIP ref codes are computed server-side and renumbered on every save.
/// </summary>
public interface ILdipService
{
    Task<IReadOnlyList<LdipRecordDto>> GetAllAsync(
        string? status, int? officeId, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDetailDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDetailDto>> CreateAsync(CreateLdipDto dto, Guid createdById, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDetailDto>> UpdateAsync(int id, UpdateLdipDto dto, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> FinalizeAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> UnlockAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>Wipes all LDIP records. Kept for post-merge dev testing. Returns deleted count.</summary>
    Task<int> PurgeAllAsync(CancellationToken ct = default);
}
