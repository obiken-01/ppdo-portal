namespace PPDO.Application.DTOs.PurchaseRequest;

/// <summary>
/// Response for POST /api/purchase-requests/import/gso-preview — RAL-196.
/// Prefill data only, nothing is persisted by parsing it. Fields the GSO export never contains
/// (Division, RequestedBy, Position, ApprovedBy, ApprovingPosition, SAINo, ALOBSNo) are always
/// null; the Create PR form leaves those for the user, same as typing a new PR by hand.
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
    IReadOnlyList<GsoPRImportItemDto> Items);

/// <summary>One item row from a parsed GSO PR export.</summary>
public sealed record GsoPRImportItemDto(
    string?  StockNo,
    string   Description,
    string   Unit,
    decimal  Quantity,
    decimal  UnitCost,
    bool     IsUnknownStock);
