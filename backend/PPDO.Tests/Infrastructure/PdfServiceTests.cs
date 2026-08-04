using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Services;
using PPDO.Tests.TestHelpers;
using static PPDO.Tests.TestHelpers.SyntheticPdfBuilder;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="PdfService"/> — written first (TDD), RAL-197.
/// Fixtures are synthetic PDFs built word-by-word via <see cref="SyntheticPdfBuilder"/> (fictional
/// values only) rather than a checked-in real signed PR PDF — see that class's doc comment.
/// Coverage mirrors <c>ExcelServiceTests</c>'s ParseGsoPRImport section.
/// </summary>
public sealed class PdfServiceTests
{
    private readonly PdfService _sut = new();

    // ── Shared fixture geometry ──────────────────────────────────────────────────
    // Column X positions for the item table body — spaced generously (>10pt gaps even for the
    // longest test values) so PdfService.SplitIntoCells reliably treats them as distinct cells.
    private const double ColItemNo  = 50;
    private const double ColStockNo = 90;
    private const double ColDesc    = 280;
    private const double ColUnit    = 460;
    private const double ColQty     = 500;
    private const double ColCost    = 540;

    /// <summary>Full happy-path fixture: title, header block, hierarchy lines, two item rows,
    /// TOTAL row, and purpose.</summary>
    private static byte[] BuildValidGsoPdf()
    {
        List<WordPlacement> w = new()
        {
            new("PURCHASE REQUEST", 220, 750, 14),

            new("PR No.: 101-1041-GF-2026-04-28-757", 50, 720),
            new("Fund: General Fund",                 50, 705),
            new("Date.: 04/28/2026",                   50, 690),

            new("Item No.   Stock No.   Description   Unit   Qty   Unit Cost   Total Cost", 50, 660, 9),

            new("1000-000-1-01-010-001 - PLANNING MONITORING AND EVALUATION PROGRAM",             50, 630, 9),
            new("1000-000-1-01-010-001-002 - Administrative Support Services",                    50, 615, 9),
            new("1000-000-1-01-010-001-002-008 - Safety measures for communicable diseases",      50, 600, 9),
            new("5 02 03 990", 50, 585, 9),

            // Item row 1
            new("1",                 ColItemNo,  555, 9),
            new("OSAME-1234567890",  ColStockNo, 555, 9),
            new("Bond Paper A4",     ColDesc,    555, 9),
            new("ream",              ColUnit,    555, 9),
            new("10",                ColQty,     555, 9),
            new("250",               ColCost,    555, 9),

            // Item row 2
            new("2",                 ColItemNo,  540, 9),
            new("OSAME-9876543210",  ColStockNo, 540, 9),
            new("Ballpen Black",     ColDesc,    540, 9),
            new("box",               ColUnit,    540, 9),
            new("5",                 ColQty,     540, 9),
            new("150",               ColCost,    540, 9),

            new("TOTAL", 50, 525, 9),

            new("Purpose: office supplies replenishment", 50, 490, 9),
        };

        return Build(w);
    }

    [Fact]
    public void ParseGsoPRImport_ValidFile_ParsesHeaderFields()
    {
        using MemoryStream stream = new(BuildValidGsoPdf());
        GsoPRImportRow result = _sut.ParseGsoPRImport(stream);

        Assert.Equal("101-1041-GF-2026-04-28-757", result.PrNo);
        Assert.Equal("General Fund", result.Fund);
        Assert.Equal(new DateOnly(2026, 4, 28), result.PRDate);
        Assert.Equal("office supplies replenishment", result.Purpose);
    }

    [Fact]
    public void ParseGsoPRImport_ValidFile_ParsesHierarchyAndAccount()
    {
        using MemoryStream stream = new(BuildValidGsoPdf());
        GsoPRImportRow result = _sut.ParseGsoPRImport(stream);

        Assert.Equal("1000-000-1-01-010-001 - PLANNING MONITORING AND EVALUATION PROGRAM", result.Program);
        Assert.Equal("1000-000-1-01-010-001-002 - Administrative Support Services", result.Project);
        Assert.Equal("1000-000-1-01-010-001-002-008 - Safety measures for communicable diseases", result.Activity);
        Assert.Equal("1000-000-1-01-010-001-002-008", result.AIPCode);
        Assert.Equal("5 02 03 990", result.AccountNo);
    }

    [Fact]
    public void ParseGsoPRImport_OrphanedActivityContinuationLine_StillFindsRealAccountCode()
    {
        // Regression test for a real-world failure: a long Activity description that wraps
        // across several PDF lines occasionally leaves one continuation line un-merged by the
        // row-wrap logic (WrapMergeGap) — that orphaned prose line has no " - " either, so the
        // old "first line without a dash" heuristic picked it over the real account code
        // sitting right below it.
        List<WordPlacement> w = new()
        {
            new("PURCHASE REQUEST", 220, 750, 14),
            new("PR No.: 101-1041-GF-2026-04-28-757", 50, 720),
            new("Fund: General Fund",                 50, 705),
            new("Date.: 04/28/2026",                   50, 690),

            new("Item No.   Stock No.   Description   Unit   Qty   Unit Cost   Total Cost", 50, 660, 9),

            new("1000-000-1-01-010-001 - PLANNING MONITORING AND EVALUATION PROGRAM",        50, 630, 9),
            new("1000-000-1-01-010-001-002 - Administrative Support Services",               50, 615, 9),
            new("1000-000-1-01-010-001-002-004 - Procurement of office supplies and",        50, 600, 9),
            // Orphaned continuation — 20pt gap from the line above (> WrapMergeGap) so it lands
            // as its own row instead of merging back into the Activity line, same as the real
            // sample PDF that triggered this bug.
            new("materials, payment of subscription, communication allowance",               50, 580, 9),
            new("5 02 03 010", 50, 565, 9), // the REAL account code

            new("1",                 ColItemNo,  535, 9),
            new("OSAME-1234567890",  ColStockNo, 535, 9),
            new("Bond Paper A4",     ColDesc,    535, 9),
            new("ream",              ColUnit,    535, 9),
            new("10",                ColQty,     535, 9),
            new("250",               ColCost,    535, 9),

            new("TOTAL", 50, 505, 9),
        };
        using MemoryStream stream = new(Build(w));

        GsoPRImportRow result = _sut.ParseGsoPRImport(stream);

        Assert.Equal("5 02 03 010", result.AccountNo);
    }

    [Fact]
    public void ParseGsoPRImport_ValidFile_ParsesItemRows()
    {
        using MemoryStream stream = new(BuildValidGsoPdf());
        GsoPRImportRow result = _sut.ParseGsoPRImport(stream);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal("OSAME-1234567890", result.Items[0].StockNo);
        Assert.Equal("Bond Paper A4", result.Items[0].Description);
        Assert.Equal("ream", result.Items[0].Unit);
        Assert.Equal(10m, result.Items[0].Quantity);
        Assert.Equal(250m, result.Items[0].UnitCost);

        Assert.Equal("OSAME-9876543210", result.Items[1].StockNo);
        Assert.Equal(5m, result.Items[1].Quantity);
        Assert.Equal(150m, result.Items[1].UnitCost);
    }

    [Fact]
    public void ParseGsoPRImport_MalformedItemRow_SkipsRatherThanThrows()
    {
        // Read-only preview, not a validated create — a bad row shouldn't block the rest of the file.
        List<WordPlacement> w = new()
        {
            new("PURCHASE REQUEST", 220, 750, 14),
            new("Item No.   Stock No.   Description   Unit   Qty", 50, 660, 9),

            new("1",          ColItemNo,  555, 9),
            new("SN-1",       ColStockNo, 555, 9),
            new("Bad Qty",    ColDesc,    555, 9),
            new("pcs",        ColUnit,    555, 9),
            new("not-a-num",  ColQty,     555, 9),

            new("2",          ColItemNo,  540, 9),
            new("SN-2",       ColStockNo, 540, 9),
            new("Good Item",  ColDesc,    540, 9),
            new("pcs",        ColUnit,    540, 9),
            new("3",          ColQty,     540, 9),
        };
        using MemoryStream stream = new(Build(w));

        GsoPRImportRow result = _sut.ParseGsoPRImport(stream);

        Assert.Single(result.Items);
        Assert.Equal("Good Item", result.Items[0].Description);
    }

    [Fact]
    public void ParseGsoPRImport_NotAGsoFile_ThrowsImportParseException()
    {
        List<WordPlacement> w = new() { new("SOME UNRELATED DOCUMENT", 50, 700, 12) };
        using MemoryStream stream = new(Build(w));

        Assert.Throws<ImportParseException>(() => _sut.ParseGsoPRImport(stream));
    }

    [Fact]
    public void ParseGsoPRImport_NoItemTable_ThrowsImportParseException()
    {
        List<WordPlacement> w = new()
        {
            new("PURCHASE REQUEST", 220, 750, 14),
            new("PR No.: 101-1041-GF-2026-04-28-757", 50, 720, 9),
            // no "Item No." header anywhere
        };
        using MemoryStream stream = new(Build(w));

        Assert.Throws<ImportParseException>(() => _sut.ParseGsoPRImport(stream));
    }

    [Fact]
    public void ParseGsoPRImport_NoItemRows_ThrowsImportParseException()
    {
        List<WordPlacement> w = new()
        {
            new("PURCHASE REQUEST", 220, 750, 14),
            new("Item No.   Description   Unit   Qty", 50, 660, 9),
            new("TOTAL", 50, 630, 9), // stops the body scan immediately — no item rows
        };
        using MemoryStream stream = new(Build(w));

        Assert.Throws<ImportParseException>(() => _sut.ParseGsoPRImport(stream));
    }
}
