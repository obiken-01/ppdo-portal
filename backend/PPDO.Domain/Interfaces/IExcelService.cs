using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Contract for all Excel (.xlsx) interactions — import and export.
/// Implemented in PPDO.Infrastructure/Services/ExcelService.cs using ClosedXML.
///
/// Three responsibilities:
///   1. GeneratePRTemplate   — Export: blank PR template for user download
///   2. ExportPRReport       — Export: filled PR Report with all three sections
///   3. ParsePRImport        — Import: parse an uploaded PR template into domain data
///
/// The Application layer (PurchaseRequestService) orchestrates loading entities
/// before calling export methods, and maps ParsePRImport results to CreatePRDto.
/// </summary>
public interface IExcelService
{
    /// <summary>
    /// Generates a blank PR Excel template (.xlsx) for user download.
    /// Yellow cells = user fills in. Gray cells = auto-fill / do not edit.
    /// One worksheet per PR — users duplicate the sheet tab for multiple PRs.
    /// Returns the file content as a byte array.
    /// </summary>
    byte[] GeneratePRTemplate();

    /// <summary>
    /// Exports a filled PR Report as an .xlsx file.
    /// Section 1: PR header fields.
    /// Section 2: Line items (PRItems).
    /// Section 3: Distribution summary (DeliveryItems + Distributions).
    ///
    /// Caller must ensure Items and Items[n].Distributions navigation
    /// properties are loaded before calling this method.
    /// Returns the file content as a byte array.
    /// </summary>
    byte[] ExportPRReport(PurchaseRequest purchaseRequest);

    /// <summary>
    /// Parses an uploaded PR Excel file (one or multiple PR sheets).
    /// Each worksheet in the uploaded file represents one PR.
    /// Unknown StockNo values are flagged — Application maps these to IsNewItem = true.
    /// Throws <see cref="ImportParseException"/> if any worksheet contains validation errors.
    /// </summary>
    /// <param name="stream">The uploaded .xlsx file stream.</param>
    /// <returns>One <see cref="PurchaseRequestImportRow"/> per valid PR worksheet.</returns>
    IReadOnlyList<PurchaseRequestImportRow> ParsePRImport(Stream stream);

    /// <summary>
    /// Parses an uploaded PR export from the external GSO system — a completely different,
    /// single-sheet layout than our own template (see docs/v1.7/GSO_PR_Import_Findings.md for
    /// the verified cell map). Always exactly one PR per file. Pure parsing only — no DB
    /// lookups; the caller (PurchaseRequestService) resolves AccountTitle and checks StockNo
    /// against ItemMaster. This is a prefill source, not a direct-create import: the GSO export
    /// never contains Division/RequestedBy/Position/ApprovedBy/ApprovingPosition/SAINo/ALOBSNo,
    /// so those always come back null on the result.
    /// Throws <see cref="ImportParseException"/> if the file isn't recognizable as a GSO PR
    /// export or has no valid item rows.
    /// </summary>
    GsoPRImportRow ParseGsoPRImport(Stream stream);

    /// <summary>
    /// Parses an uploaded stock-balance bulk-upload workbook (RAL-193) — single sheet,
    /// row 1 is the header, columns A-H: StockNo | CountedQty | EffectiveDate | Reason |
    /// Description | Unit | UnitCost | ItemType. Reason/Description/Unit/UnitCost/ItemType
    /// are all optional in the file — Description/Unit are only required when the StockNo
    /// turns out not to be cataloged yet (validated by StockBalanceService at commit time,
    /// same as the Description/Unit fields on the manual entry form). Pure parsing only — no
    /// DB lookups, no permission checks.
    /// Every non-blank row is returned, even invalid ones (each carries its own optional
    /// <see cref="StockBalanceImportRow.Error"/>), so the caller can show a full preview
    /// instead of rejecting the whole file over one bad row. Stops at the first fully
    /// blank row.
    /// </summary>
    IReadOnlyList<StockBalanceImportRow> ParseStockBalanceImport(Stream stream);
}

// ── Import data models ────────────────────────────────────────────────────────
// Defined here (alongside the interface) to keep the Domain layer self-contained.
// Application layer maps these to CreatePRDto / CreatePRItemDto.

/// <summary>
/// Raw data parsed from one PR worksheet in an uploaded Excel template.
/// Application layer validates and maps this to a CreatePRDto.
/// </summary>
public sealed record PurchaseRequestImportRow
{
    /// <summary>The worksheet tab name — used as the PR reference during import.</summary>
    public required string SheetName { get; init; }

    /// <summary>
    /// Optional PR No. supplied by the sheet. Null when left blank — the backend then
    /// auto-generates one, exactly like leaving PR No. blank on the manual Create PR form.
    /// </summary>
    public string? PrNo { get; init; }

    // ── Section 1 — PR header ─────────────────────────────────────────────────

    /// <summary>Raw division text parsed from the sheet — resolved to a division id at import time.</summary>
    public required string DivisionName { get; init; }
    public required string RequestedBy { get; init; }
    public required DateOnly PRDate { get; init; }
    public string Department { get; init; } = "PPDO";
    public string? Fund { get; init; }
    public string? Position { get; init; }
    public string? ApprovedBy { get; init; }
    public string? ApprovingPosition { get; init; }
    public string? AIPCode { get; init; }
    public string? AccountNo { get; init; }
    public string? AccountTitle { get; init; }
    public string? Program { get; init; }
    public string? Project { get; init; }
    public string? Activity { get; init; }
    public string? SAINo { get; init; }
    public string? ALOBSNo { get; init; }

    // ── Section 2 — Line items ────────────────────────────────────────────────

    public required IReadOnlyList<PRItemImportRow> Items { get; init; }
}

/// <summary>
/// Raw data parsed from an uploaded GSO-system PR export — either the .xlsx or the signed
/// .pdf export (RAL-197), both parsed to this same shape. Every field except <see cref="Items"/>
/// is nullable — the export's own layout only ever supplies a subset of what a PR needs, and
/// this is a prefill preview, not a validated create. See docs/v1.7/GSO_PR_Import_Findings.md
/// for exactly what each source format does and doesn't have.
/// </summary>
public sealed record GsoPRImportRow
{
    public string?  PrNo      { get; init; }
    public string?  Fund      { get; init; }
    public DateOnly? PRDate   { get; init; }
    public string?  Purpose   { get; init; }
    public string?  AIPCode   { get; init; }
    public string?  AccountNo { get; init; }
    public string?  Program   { get; init; }
    public string?  Project   { get; init; }
    public string?  Activity  { get; init; }

    public required IReadOnlyList<PRItemImportRow> Items { get; init; }
}

/// <summary>
/// Raw data for one item row parsed from the Section 2 grid of an uploaded PR template.
/// </summary>
public sealed record PRItemImportRow
{
    /// <summary>Stock number. Null if not found in the template row.</summary>
    public string? StockNo { get; init; }

    public required string Description { get; init; }
    public required string Unit { get; init; }
    public required decimal Quantity { get; init; }
    public decimal UnitCost { get; init; }

    /// <summary>
    /// True when the StockNo was not found in ItemMaster during parsing.
    /// Application layer sets IsNewItem = true on the resulting ItemMaster entry.
    /// </summary>
    public bool IsUnknownStock { get; init; }
}

/// <summary>
/// Raw data for one row parsed from an uploaded stock-balance bulk-upload workbook
/// (RAL-193). <see cref="RowNumber"/> is the 1-based Excel row (header is row 1, so data
/// starts at row 2) — used to point the user back at the offending row in the preview.
/// </summary>
public sealed record StockBalanceImportRow
{
    public required int RowNumber { get; init; }
    public string? StockNo { get; init; }
    public decimal? CountedQty { get; init; }
    public DateOnly? EffectiveDate { get; init; }
    public string? Reason { get; init; }

    /// <summary>Description/Unit/UnitCost/ItemType — only used when StockNo isn't already in
    /// Items Master, to auto-create it (mirrors the GSO PR import's unknown-stock handling).
    /// Description and Unit aren't validated here (parsing has no DB access to know whether
    /// the StockNo is actually unknown) — StockBalanceService validates them at commit time.</summary>
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public decimal? UnitCost { get; init; }
    public string? ItemType { get; init; }

    /// <summary>Set when this row is unusable (missing StockNo, unparseable quantity/date).
    /// Null means the row parsed cleanly.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Thrown by <see cref="IExcelService.ParsePRImport"/> when the uploaded file
/// contains validation errors (missing required fields, bad date formats, etc.).
/// The <see cref="Errors"/> list contains one entry per problem found.
/// The entire file is rejected — partial imports are not allowed.
/// </summary>
public sealed class ImportParseException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ImportParseException(IReadOnlyList<string> errors)
        : base($"Excel import validation failed with {errors.Count} error(s).")
    {
        Errors = errors;
    }
}
