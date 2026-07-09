using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Procurement preset CRUD (v1.4 WFP Rework — RAL-119). Account-scoped, reusable
/// procurement line-item templates, shared across all offices/divisions. No CSV
/// import/export — presets are captured from real entries or curated one-by-one on the
/// config page, never bulk-imported.
///
/// Item snapshot semantics: when an item's PriceIndexItemId is given, Name/Unit/UnitPrice
/// are re-snapshotted server-side from the current price index row on every save — later
/// price-index updates never retroactively change an already-saved preset.
///
/// Reachable from two callers with different permission gates (RAL-119's Functions layer):
/// the config-page CRUD (CanManageConfig) and a lighter "quick save" used by the WFP entry
/// wizard (CanAccessBudgetPlanning) — both go through <see cref="CreateAsync"/>.
/// </summary>
public interface IProcurementPresetService
{
    /// <summary>
    /// Presets scoped to one account, optionally filtered by active status. When
    /// <paramref name="accountId"/> is null, returns presets across ALL accounts (the config
    /// page's default view), ordered by account number then name.
    /// </summary>
    Task<IReadOnlyList<ProcurementPresetDto>> GetByAccountAsync(
        int? accountId, ActiveFilter active, CancellationToken cancellationToken = default);

    Task<ServiceResult<ProcurementPresetDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates a new preset. <paramref name="caller"/> is stamped as CreatedBy.</summary>
    Task<ServiceResult<ProcurementPresetDto>> CreateAsync(
        User caller, UpsertProcurementPresetDto dto, CancellationToken cancellationToken = default);

    /// <summary>Replaces name/account/active flag and the item set (delete-then-reinsert). CreatedBy is never changed.</summary>
    Task<ServiceResult<ProcurementPresetDto>> UpdateAsync(
        int id, UpsertProcurementPresetDto dto, CancellationToken cancellationToken = default);

    /// <summary>Soft delete (IsActive = false).</summary>
    Task<ServiceResult<ProcurementPresetDto>> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
