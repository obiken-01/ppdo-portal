using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// LDIP (Local Development Investment Program) CRUD + status lifecycle (RAL-64).
/// Status workflow: Draft → Final (finalize) → Draft (unlock, admin only) → Archived.
/// </summary>
public interface ILdipService
{
    Task<IReadOnlyList<LdipRecordDto>> GetAllAsync(string? status, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> GetByIdAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> CreateAsync(CreateLdipDto dto, Guid createdById, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> UpdateAsync(int id, UpdateLdipDto dto, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> FinalizeAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> UnlockAsync(int id, CancellationToken ct = default);
    Task<ServiceResult<LdipRecordDto>> ArchiveAsync(int id, CancellationToken ct = default);

    /// <summary>Wipes all LDIP records. Kept for post-merge dev testing. Returns deleted count.</summary>
    Task<int> PurgeAllAsync(CancellationToken ct = default);
}
