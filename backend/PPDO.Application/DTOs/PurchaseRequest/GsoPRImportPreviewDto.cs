namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// Response for POST /api/purchase-requests/import/gso-preview — RAL-196/RAL-197.
/// Prefill data only, nothing is persisted by parsing it. Division and SAINo/ALOBSNo are always
/// null — neither source format ever has them, so the Create PR form leaves those for the user,
/// same as typing a new PR by hand. RequestedBy/Position/ApprovedBy/ApprovingPosition are null
/// when the source was the .xlsx export (which never has them) and populated when the source was
/// the signed .pdf export (its signature block).
/// </summary>
public sealed record GsoPRImportPreviewDto(
    string?   PrNo,
    string?   Fund,
    DateOnly? PRDate,
    string?   Purpose,
    string?   AIPCode,
    string?   AccountNo,
    string?   AccountTitle,
    string?   Program,
    string?   Project,
    string?   Activity,
    string?   RequestedBy,
    string?   Position,
    string?   ApprovedBy,
    string?   ApprovingPosition,
    IReadOnlyList<GsoPRImportItemDto> Items);

/// <summary>One item row from a parsed GSO PR export.</summary>
public sealed record GsoPRImportItemDto(
    string?  StockNo,
    string   Description,
    string   Unit,
    decimal  Quantity,
    decimal  UnitCost,
    bool     IsUnknownStock);
