namespace PPDO.Application.DTOs.Distribution;

/// <summary>
/// One division's share of an item-level distribution request — one row from the
/// Distribution page's split form. The backend allocates it across the item's
/// available delivery batches (FIFO, oldest DeliveryDate first).
/// </summary>
public sealed record DistributionSplitDto
{
    public required string   Division   { get; init; }
    public required decimal  QtyIssued  { get; init; }
    public required DateOnly DateIssued { get; init; }
    public required string   IssuedBy   { get; init; }
    public string?           Remarks    { get; init; }
}

/// <summary>
/// Request body for POST /api/distributions/item/{stockNo}/allocate.
/// Each split may be satisfied by more than one delivery batch — the service creates
/// one Distribution record per (split × batch) pair it draws from.
/// </summary>
public sealed record CreateItemDistributionDto
{
    public required IReadOnlyList<DistributionSplitDto> Splits { get; init; }
}
