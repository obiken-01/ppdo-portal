using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Procurement preset CRUD (v1.4 WFP Rework — RAL-119). Mirrors the delete-then-reinsert
/// child-collection convention used by <see cref="WfpExpenditureService"/>: updating a preset
/// replaces its item set wholesale rather than diffing rows.
/// </summary>
public sealed class ProcurementPresetService : IProcurementPresetService
{
    private readonly IProcurementPresetRepository       _repo;
    private readonly IRepository<ProcurementPresetItem> _itemRepo;
    private readonly IRepository<Account>                _accountRepo;
    private readonly IRepository<PriceIndexItem>         _priceIndexRepo;
    private readonly IAuditService                       _audit;
    private readonly ILogger<ProcurementPresetService>   _logger;

    public ProcurementPresetService(
        IProcurementPresetRepository       repo,
        IRepository<ProcurementPresetItem> itemRepo,
        IRepository<Account>               accountRepo,
        IRepository<PriceIndexItem>        priceIndexRepo,
        IAuditService                       audit,
        ILogger<ProcurementPresetService>  logger)
    {
        _repo           = repo;
        _itemRepo       = itemRepo;
        _accountRepo    = accountRepo;
        _priceIndexRepo = priceIndexRepo;
        _audit          = audit;
        _logger         = logger;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProcurementPresetDto>> GetByAccountAsync(
        int accountId, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ProcurementPreset> presets = await _repo.GetByAccountIdAsync(accountId, cancellationToken);

        IEnumerable<ProcurementPreset> q = active switch
        {
            ActiveFilter.Active   => presets.Where(p => p.IsActive),
            ActiveFilter.Inactive => presets.Where(p => !p.IsActive),
            _                     => presets,
        };

        return q.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProcurementPresetDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        ProcurementPreset? entity = await _repo.GetByIntIdAsync(id, cancellationToken);
        return entity is null
            ? ServiceResult<ProcurementPresetDto>.NotFound($"Procurement preset {id} not found.")
            : ServiceResult<ProcurementPresetDto>.Ok(MapToDto(entity));
    }

    // ── Create ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<ProcurementPresetDto>> CreateAsync(
        User caller, UpsertProcurementPresetDto dto, CancellationToken cancellationToken = default)
    {
        string? validationError = await ValidateAsync(dto, cancellationToken);
        if (validationError is not null)
            return ServiceResult<ProcurementPresetDto>.BadRequest(validationError);

        DateTime now = DateTime.UtcNow;
        ProcurementPreset entity = new()
        {
            AccountId   = dto.AccountId,
            Name        = dto.Name.Trim(),
            IsActive    = dto.IsActive,
            CreatedById = caller.Id,
            CreatedAt   = now,
            UpdatedAt   = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken); // generate entity.Id before items reference it

        await AddItemsAsync(entity.Id, dto.Items, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Procurement preset created. Name: {Name}, AccountId: {AccountId}", entity.Name, entity.AccountId);
        await _audit.LogAsync("procurement_presets", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.Name, entity.AccountId, entity.IsActive },
            cancellationToken);

        ProcurementPreset? saved = await _repo.GetByIntIdAsync(entity.Id, cancellationToken);
        return ServiceResult<ProcurementPresetDto>.Ok(MapToDto(saved!));
    }

    // ── Update ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<ProcurementPresetDto>> UpdateAsync(
        int id, UpsertProcurementPresetDto dto, CancellationToken cancellationToken = default)
    {
        ProcurementPreset? entity = await _repo.GetByIntIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<ProcurementPresetDto>.NotFound($"Procurement preset {id} not found.");

        string? validationError = await ValidateAsync(dto, cancellationToken);
        if (validationError is not null)
            return ServiceResult<ProcurementPresetDto>.BadRequest(validationError);

        var oldSnapshot = new { entity.Name, entity.AccountId, entity.IsActive };

        entity.AccountId = dto.AccountId;
        entity.Name      = dto.Name.Trim();
        entity.IsActive  = dto.IsActive;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);

        // Delete-then-reinsert children (WfpExpenditureService convention) so the item set
        // never sees old+new rows side by side.
        foreach (ProcurementPresetItem existingItem in entity.Items.ToList())
            await _itemRepo.DeleteAsync(existingItem, cancellationToken);

        await AddItemsAsync(entity.Id, dto.Items, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync("procurement_presets", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.Name, entity.AccountId, entity.IsActive },
            cancellationToken);

        ProcurementPreset? saved = await _repo.GetByIntIdAsync(entity.Id, cancellationToken);
        return ServiceResult<ProcurementPresetDto>.Ok(MapToDto(saved!));
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<ProcurementPresetDto>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        ProcurementPreset? entity = await _repo.GetByIntIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<ProcurementPresetDto>.NotFound($"Procurement preset {id} not found.");

        entity.IsActive  = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Procurement preset deactivated. Id: {Id}, Name: {Name}", entity.Id, entity.Name);
        await _audit.LogAsync("procurement_presets", entity.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);

        return ServiceResult<ProcurementPresetDto>.Ok(MapToDto(entity));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private async Task<string?> ValidateAsync(UpsertProcurementPresetDto dto, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return "Name is required.";

        IReadOnlyList<Account> accounts = await _accountRepo.GetAllAsync(ct);
        if (!accounts.Any(a => a.Id == dto.AccountId))
            return $"Account {dto.AccountId} not found.";

        if (dto.Items.Count == 0)
            return "At least one item is required.";

        IReadOnlyList<PriceIndexItem> priceIndex = await _priceIndexRepo.GetAllAsync(ct);
        foreach (UpsertProcurementPresetItemDto item in dto.Items)
        {
            if (item.DefaultQty < 0)
                return "Item default quantity cannot be negative.";

            if (item.PriceIndexItemId.HasValue)
            {
                if (!priceIndex.Any(p => p.Id == item.PriceIndexItemId.Value))
                    return $"Price index item {item.PriceIndexItemId.Value} not found.";
            }
            else
            {
                if (string.IsNullOrWhiteSpace(item.Name))
                    return "Item name is required when no price index item is picked.";
                if (string.IsNullOrWhiteSpace(item.Unit))
                    return "Item unit is required when no price index item is picked.";
                if (item.UnitPrice is null || item.UnitPrice < 0)
                    return "Item unit price is required and cannot be negative when no price index item is picked.";
            }
        }

        return null;
    }

    // ── Item snapshot + persistence ──────────────────────────────────────────

    private async Task AddItemsAsync(
        int presetId, IReadOnlyList<UpsertProcurementPresetItemDto> items, CancellationToken ct)
    {
        IReadOnlyList<PriceIndexItem> priceIndex = await _priceIndexRepo.GetAllAsync(ct);

        foreach (UpsertProcurementPresetItemDto item in items)
        {
            PriceIndexItem? source = item.PriceIndexItemId.HasValue
                ? priceIndex.FirstOrDefault(p => p.Id == item.PriceIndexItemId.Value)
                : null;

            await _itemRepo.AddAsync(new ProcurementPresetItem
            {
                PresetId         = presetId,
                PriceIndexItemId = item.PriceIndexItemId,
                Name             = source?.Name ?? item.Name!.Trim(),
                Unit             = source?.Unit ?? item.Unit!.Trim(),
                UnitPrice        = source?.UnitPrice ?? item.UnitPrice!.Value,
                DefaultQty       = item.DefaultQty,
            }, ct);
        }
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static ProcurementPresetDto MapToDto(ProcurementPreset entity) => new(
        entity.Id,
        entity.AccountId,
        entity.Account?.AccountNumber,
        entity.Account?.AccountTitle,
        entity.Name,
        entity.IsActive,
        entity.CreatedById,
        entity.CreatedBy?.FullName,
        entity.CreatedAt,
        entity.UpdatedAt,
        entity.Items.Select(i => new ProcurementPresetItemDto(
            i.Id, i.PriceIndexItemId, i.Name, i.Unit, i.UnitPrice, i.DefaultQty)).ToList());
}
