namespace PPDO.Application.DTOs.Config;

/// <summary>Read model for one procurement preset line item.</summary>
public sealed record ProcurementPresetItemDto(
    int      Id,
    int?     PriceIndexItemId,
    string   Name,
    string   Unit,
    decimal  UnitPrice,
    decimal  DefaultQty);

/// <summary>Read model for a procurement preset, with its items expanded.</summary>
public sealed record ProcurementPresetDto(
    int      Id,
    int      AccountId,
    string?  AccountNumber,
    string?  AccountTitle,
    string   Name,
    bool     IsActive,
    Guid     CreatedById,
    string?  CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    IReadOnlyList<ProcurementPresetItemDto> Items);

/// <summary>
/// Create/update body for one preset line item. When <see cref="PriceIndexItemId"/> is set,
/// Name/Unit/UnitPrice are ignored and re-snapshotted server-side from the current price index
/// row. When null, Name/Unit/UnitPrice are required free-typed values.
/// </summary>
public sealed record UpsertProcurementPresetItemDto(
    int?     PriceIndexItemId,
    string?  Name,
    string?  Unit,
    decimal? UnitPrice,
    decimal  DefaultQty);

/// <summary>Create/update body for a procurement preset.</summary>
public sealed record UpsertProcurementPresetDto(
    int      AccountId,
    string   Name,
    bool     IsActive,
    IReadOnlyList<UpsertProcurementPresetItemDto> Items);
