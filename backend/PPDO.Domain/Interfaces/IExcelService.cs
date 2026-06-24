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
    /// Throws <see cref="ExcelParseException"/> if any worksheet contains validation errors.
    /// </summary>
    /// <param name="stream">The uploaded .xlsx file stream.</param>
    /// <returns>One <see cref="PurchaseRequestImportRow"/> per valid PR worksheet.</returns>
    IReadOnlyList<PurchaseRequestImportRow> ParsePRImport(Stream stream);
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
/// Thrown by <see cref="IExcelService.ParsePRImport"/> when the uploaded file
/// contains validation errors (missing required fields, bad date formats, etc.).
/// The <see cref="Errors"/> list contains one entry per problem found.
/// The entire file is rejected — partial imports are not allowed.
/// </summary>
public sealed class ExcelParseException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public ExcelParseException(IReadOnlyList<string> errors)
        : base($"Excel import validation failed with {errors.Count} error(s).")
    {
        Errors = errors;
    }
}
