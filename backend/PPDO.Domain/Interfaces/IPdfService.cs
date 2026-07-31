namespace PPDO.Domain.Interfaces;

/// <summary>
/// Contract for parsing the signed PDF export of a GSO-system Purchase Request (RAL-197).
/// Implemented in PPDO.Infrastructure/Services/PdfService.cs using PdfPig.
///
/// Same purpose and same result shape as <see cref="IExcelService.ParseGsoPRImport"/> — the two
/// GSO export formats (xlsx and pdf) parse to the identical <see cref="GsoPRImportRow"/>, so
/// PurchaseRequestService.PreviewGsoImportAsync can dispatch to either without caring which one
/// it got. The PDF uniquely supplies RequestedBy/Position/ApprovedBy/ApprovingPosition (the
/// signed document's signature block) — the xlsx export never has those.
/// </summary>
public interface IPdfService
{
    /// <summary>
    /// Parses an uploaded GSO-system PR PDF export. Always exactly one PR per file. Pure
    /// parsing only — no DB lookups; the caller resolves AccountTitle and checks StockNo
    /// against ItemMaster, same as the xlsx path.
    /// Throws <see cref="ImportParseException"/> if the file isn't recognizable as a GSO PR PDF
    /// export or has no valid item rows.
    /// </summary>
    GsoPRImportRow ParseGsoPRImport(Stream stream);
}
