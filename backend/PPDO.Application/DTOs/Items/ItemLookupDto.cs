namespace PPDO.Application.DTOs.Items;

/// <summary>
/// Lightweight response for <c>GET /api/items/lookup?term=</c>.
/// Returns enough data to populate the Create PR form autocomplete
/// (StockNo ↔ Description bidirectional lookup).
/// </summary>
public sealed record ItemLookupDto(
    Guid    Id,
    string  StockNo,
    string  Description,
    string  Unit,
    decimal UnitCost);
