namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>A single line item submitted as part of a Create PR or Update PR request.</summary>
public sealed record CreatePRItemDto
{
    public string? StockNo { get; init; }
    public required string Description { get; init; }
    public required string Unit { get; init; }
    public required decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }
    public string? ItemType { get; init; }
}
