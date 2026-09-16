using ClosedXML.Excel;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The Annex B workbook writer (V18-60 / PPDO-84, <c>AIP_Form_Spec.md</c> Part II §13). Every test
/// reads the saved file back with ClosedXML.
///
/// <para>
/// ⚠️ <b>The formulas are checked by their calculated values</b>, not only by their text: the office
/// subtotals and TOTAL are <c>SUM</c> formulas in the file but figures from the builder on the grid,
/// and a formula over the wrong range would still look like a formula.
/// </para>
///
/// The sample GENERAL sheet lays out as: 10 office A · 11 program · 12 project · 13–14 activities ·
/// 15 office B · 16 program · 17 activity · 18 TOTAL · 19–20 note.
/// </summary>
public sealed class AipFormExcelServiceTests
{
    private readonly AipFormExcelService _sut = new();

    private static AipPrintedAmountsDto Amounts(
        decimal ps = 0m, decimal mooe = 0m, decimal co = 0m, decimal cca = 0m, decimal ccm = 0m)
        => new(ps, mooe, co, ps + mooe + co, cca, ccm);

    private static AipConsolidatedRowDto Heading(string kind, string refCode, string name)
        => new(kind, refCode, name, null, null, null, null, null, null, null, null, null, null);

    private static AipConsolidatedRowDto Office(string refCode, string name, AipPrintedAmountsDto amounts)
        => new(AipFormRowBuilder.OfficeRow, refCode, name, null, "SubmittedToPpdo",
            null, null, null, null, null, null, null, amounts);

    private static AipConsolidatedRowDto Activity(
        int id, string refCode, string name, AipPrintedAmountsDto amounts,
        string? esre = null, string? funding = null, string? typology = null)
        => new(AipFormRowBuilder.ActivityRow, refCode, name, id, null, esre, "PGO", "January", "December",
            "12 positions filled", funding, typology, amounts);

    private static readonly AipPrintedAmountsDto Act401 = Amounts(ps: 2_000m, mooe: 1_301_000m);
    private static readonly AipPrintedAmountsDto Act402 = Amounts(mooe: 2_000m);
    private static readonly AipPrintedAmountsDto Act403 = Amounts(co: 2_000m, cca: 2_000m);
    private static readonly AipPrintedAmountsDto OfficeA = AipPrintedFigures.Sum([Act401, Act402]);
    private static readonly AipPrintedAmountsDto OfficeB = Act403;
    private static readonly AipPrintedAmountsDto SheetTotal = AipPrintedFigures.Sum([OfficeA, OfficeB]);

    private static AipFormWorkbookDto Sample(int submitted = 2, int total = 5)
    {
        List<AipConsolidatedRowDto> general =
        [
            Office("1000-000-1-01-001", "OFFICE OF THE PROVINCIAL GOVERNOR", OfficeA),
            Heading(AipFormRowBuilder.ProgramRow, "1000-000-1-01-001-001", "EXECUTIVE GOVERNANCE PROGRAM"),
            Heading(AipFormRowBuilder.ProjectRow, "1000-000-1-01-001-001-001", "General supervision"),
            Activity(401, "1000-000-1-01-001-001-001-001", "Plantilla positions", Act401, esre: "ID", funding: "GF/20% DF"),
            Activity(402, "1000-000-1-01-001-001-001-002", "Admin support", Act402, funding: "GF"),
            Office("1000-000-1-01-010", "PROVINCIAL PLANNING AND DEVELOPMENT OFFICE", OfficeB),
            Heading(AipFormRowBuilder.ProgramRow, "1000-000-1-01-010-001", "PLANNING PROGRAM"),
            Activity(403, "1000-000-1-01-010-001-001-001", "GIS workstations", Act403, typology: "A1"),
        ];

        return new AipFormWorkbookDto(
            2028, new DateOnly(2026, 9, 14), submitted, total,
            [
                new AipFormWorkbookSheetDto(AipSector.General, general, SheetTotal),
                new AipFormWorkbookSheetDto(AipSector.Social, [], AipPrintedFigures.Zero),
                new AipFormWorkbookSheetDto(AipSector.Economic, [], AipPrintedFigures.Zero),
                new AipFormWorkbookSheetDto(AipSector.Others, [], AipPrintedFigures.Zero),
            ]);
    }

    private XLWorkbook Open(AipFormWorkbookDto? workbook = null)
        => new(new MemoryStream(_sut.Export(workbook ?? Sample())));

    private static string? MergeOf(IXLWorksheet ws, string cell)
        => ws.Cell(cell).IsMerged() ? ws.Cell(cell).MergedRange().RangeAddress.ToStringRelative() : null;

    private static double Number(IXLWorksheet ws, string cell) => ws.Cell(cell).Value.GetNumber();

    // ── Workbook and preamble ─────────────────────────────────────────────────

    [Fact]
    public void Export_WritesOneSheetPerSector_NamedForTheYear()
    {
        using XLWorkbook wb = Open();

        Assert.Equal(
            ["GENERAL_FY2028", "SOCIAL_FY2028", "ECONOMIC_FY2028", "OTHERS_FY2028"],
            wb.Worksheets.Select(ws => ws.Name));
    }

    [Fact]
    public void Export_Preamble_MatchesTheProvincesForm()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.Equal("Annex B", ws.Cell("A1").GetString());
        Assert.Equal("ANNUAL INVESTMENT PROGRAM (AIP) FY 2028", ws.Cell("A2").GetString());
        Assert.Equal("By Program/Project/Activity by Sector", ws.Cell("A3").GetString());
        Assert.Equal("As of SEPTEMBER 2026", ws.Cell("A4").GetString());
        Assert.Equal("Province/City/Municipality/Barangay: OCCIDENTAL MINDORO", ws.Cell("A6").GetString());
        Assert.Equal("A2:R2", MergeOf(ws, "A2"));
    }

    /// <summary>§11 partial season: row 5 says how many offices are in, on every sheet.</summary>
    [Fact]
    public void Export_PartialSeason_Row5CarriesTheCompletenessLine()
    {
        using XLWorkbook wb = Open(Sample(submitted: 2, total: 5));

        Assert.All(wb.Worksheets, ws =>
        {
            Assert.Equal(
                "Includes 2 of 5 offices — offices still being prepared or reviewed are not in these totals.",
                ws.Cell("A5").GetString());
            Assert.Equal("A5:R5", MergeOf(ws, "A5"));
        });
    }

    /// <summary>§11 complete season: row 5 is the province's blank spacer.</summary>
    [Fact]
    public void Export_CompleteSeason_Row5IsBlank()
    {
        using XLWorkbook wb = Open(Sample(submitted: 5, total: 5));

        Assert.True(wb.Worksheet("GENERAL_FY2028").Cell("A5").IsEmpty());
    }

    // ── Header ────────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Header_IsTheTwoTierAnnexBHeader()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.Equal("A8:A9", MergeOf(ws, "A8"));
        Assert.Equal("B8:E9", MergeOf(ws, "B8"));
        Assert.Equal("H8:I8", MergeOf(ws, "H8"));
        Assert.Equal("L8:O8", MergeOf(ws, "L8"));
        Assert.Equal("P8:R8", MergeOf(ws, "P8"));

        Assert.Contains("(2)", ws.Cell("B8").GetString());
        Assert.Contains("(9)", ws.Cell("M9").GetString());
        Assert.Contains("(In Thousand Pesos)", ws.Cell("L8").GetString());
        Assert.Contains("(In Thousand Pesos)", ws.Cell("P8").GetString());
    }

    /// <summary>⚠️ §3: eSRE is a provincial insertion — no DBM column number.</summary>
    [Fact]
    public void Export_Header_ColumnFCarriesNoDbmNumber()
    {
        using XLWorkbook wb = Open();

        Assert.Equal("eSRE Code", wb.Worksheet("GENERAL_FY2028").Cell("F8").GetString());
    }

    // ── Body ──────────────────────────────────────────────────────────────────

    /// <summary>§4: the description column IS the level — B office, C program, D project, E activity.</summary>
    [Fact]
    public void Export_Body_WritesEachLevelInItsOwnDescriptionColumn()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.Equal("OFFICE OF THE PROVINCIAL GOVERNOR", ws.Cell("B10").GetString());
        Assert.Equal("1000-000-1-01-001", ws.Cell("A10").GetString());

        Assert.Equal("EXECUTIVE GOVERNANCE PROGRAM", ws.Cell("C11").GetString());
        Assert.Equal("C11:E11", MergeOf(ws, "C11"));

        Assert.Equal("General supervision", ws.Cell("D12").GetString());
        Assert.Equal("D12:E12", MergeOf(ws, "D12"));

        Assert.Equal("Plantilla positions", ws.Cell("E13").GetString());
        Assert.True(ws.Cell("B13").IsEmpty() && ws.Cell("C13").IsEmpty() && ws.Cell("D13").IsEmpty());

        Assert.Equal("TOTAL", ws.Cell("A18").GetString());
    }

    /// <summary>
    /// B–E read as one description cell, so no vertical rule is drawn between them — on activity rows
    /// or TOTAL. ⚠️ Checked per cell: a range-level border call only touches the range's outer edge.
    /// </summary>
    [Fact]
    public void Export_DescriptionColumns_HaveNoInnerVerticalRules()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        foreach (int row in new[] { 13, 18 })
        {
            foreach (string col in new[] { "B", "C", "D" })
                Assert.Equal(XLBorderStyleValues.None, ws.Cell($"{col}{row}").Style.Border.RightBorder);
            foreach (string col in new[] { "C", "D", "E" })
                Assert.Equal(XLBorderStyleValues.None, ws.Cell($"{col}{row}").Style.Border.LeftBorder);
        }
        Assert.Equal(XLBorderStyleValues.Thin, ws.Cell("E13").Style.Border.RightBorder);
    }

    /// <summary>
    /// A program or project code is longer than column A — it wraps on every level, never clips — and
    /// sits centred, as in the province's file.
    /// </summary>
    [Fact]
    public void Export_RefCodes_WrapAndCentreOnEveryLevel()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        foreach (string cell in new[] { "A10", "A11", "A12", "A13" })
        {
            Assert.True(ws.Cell(cell).Style.Alignment.WrapText, $"{cell} should wrap");
            Assert.Equal(XLAlignmentHorizontalValues.Center, ws.Cell(cell).Style.Alignment.Horizontal);
        }
        Assert.Contains("Office/ Department", ws.Cell("G8").GetString());
    }

    [Fact]
    public void Export_ActivityRow_WritesItsTextColumns()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.Equal("ID", ws.Cell("F13").GetString());
        Assert.Equal("PGO", ws.Cell("G13").GetString());
        Assert.Equal("January", ws.Cell("H13").GetString());
        Assert.Equal("December", ws.Cell("I13").GetString());
        Assert.Equal("12 positions filled", ws.Cell("J13").GetString());
        Assert.Equal("GF/20% DF", ws.Cell("K13").GetString());
        Assert.Equal("A1", ws.Cell("R17").GetString());
    }

    /// <summary>Decision 6: amounts in thousands — ₱1,301,000 is written 1301. Activity amounts are values, not formulas.</summary>
    [Fact]
    public void Export_ActivityAmounts_AreValuesInThousands()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.False(ws.Cell("M13").HasFormula);
        Assert.Equal(1301d, Number(ws, "M13"));
        Assert.Equal(2d, Number(ws, "L13"));
        Assert.Equal(2d, Number(ws, "P17"));
    }

    /// <summary>
    /// ↩️ Program and project rows carried blank amounts until PPDO-98; they now carry the subtotal of
    /// the activities beneath them, and it calculates to the same figure the office row does when the
    /// office has only the one program.
    /// </summary>
    [Fact]
    public void Export_HeadingRows_CarryTheirSubtotal()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        // Office A's program (11) and project (12) both hold its two activities and nothing else.
        foreach (string row in new[] { "11", "12" })
        {
            Assert.Equal((double)(OfficeA.Ps    / 1000m), Number(ws, $"L{row}"));
            Assert.Equal((double)(OfficeA.Mooe  / 1000m), Number(ws, $"M{row}"));
            Assert.Equal((double)(OfficeA.Total / 1000m), Number(ws, $"O{row}"));
        }

        // ⚠️ Office B's activity hangs off a SYNTHETIC project, which prints no row — its figures
        // still have to reach the program heading, or the heading would total less than the row under it.
        Assert.Equal((double)(OfficeB.Co           / 1000m), Number(ws, "N16"));
        Assert.Equal((double)(OfficeB.CcAdaptation / 1000m), Number(ws, "P16"));
    }

    /// <summary>
    /// A heading with nothing costed under it stays blank, not ₱0 — the distinction the null
    /// <c>Amounts</c> was always for. The office row above it is still a figure: an office row is a
    /// submission, and reads as zero.
    /// </summary>
    [Fact]
    public void Export_AHeadingWithNoActivity_LeavesItsAmountsBlank()
    {
        AipFormWorkbookDto workbook = new(
            2028, new DateOnly(2026, 9, 14), 1, 5,
            [
                new AipFormWorkbookSheetDto(AipSector.General,
                    [
                        Office("1000-000-1-01-001", "OFFICE OF THE PROVINCIAL GOVERNOR", AipPrintedFigures.Zero),
                        Heading(AipFormRowBuilder.ProgramRow, "1000-000-1-01-001-001", "EXECUTIVE GOVERNANCE PROGRAM"),
                        Heading(AipFormRowBuilder.ProjectRow, "1000-000-1-01-001-001-001", "Not costed yet"),
                    ],
                    AipPrintedFigures.Zero),
                new AipFormWorkbookSheetDto(AipSector.Social,   [], AipPrintedFigures.Zero),
                new AipFormWorkbookSheetDto(AipSector.Economic, [], AipPrintedFigures.Zero),
                new AipFormWorkbookSheetDto(AipSector.Others,   [], AipPrintedFigures.Zero),
            ]);

        using XLWorkbook wb = Open(workbook);
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        foreach (string col in new[] { "L", "M", "N", "O", "P", "Q" })
        {
            Assert.True(ws.Cell($"{col}12").IsEmpty(), $"{col}12 (project, nothing under it) should be blank");
            Assert.True(ws.Cell($"{col}11").IsEmpty(), $"{col}11 (program, nothing costed) should be blank");
            Assert.Equal(0d, Number(ws, $"{col}10"));
        }
    }

    /// <summary>
    /// ⚠️ Decision 4: every formula's calculated value equals the builder's figure — activity Total,
    /// both office subtotals and the sheet TOTAL, on every amount column.
    /// </summary>
    [Fact]
    public void Export_Formulas_CalculateToTheBuildersFigures()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.True(ws.Cell("O13").HasFormula);
        Assert.Equal((double)(Act401.Total / 1000m), Number(ws, "O13"));
        Assert.Equal((double)(Act402.Total / 1000m), Number(ws, "O14"));
        Assert.Equal((double)(Act403.Total / 1000m), Number(ws, "O17"));

        (string Row, AipPrintedAmountsDto Expected)[] summed = [("10", OfficeA), ("15", OfficeB), ("18", SheetTotal)];
        foreach ((string row, AipPrintedAmountsDto expected) in summed)
        {
            decimal[] figures = [expected.Ps, expected.Mooe, expected.Co, expected.Total, expected.CcAdaptation, expected.CcMitigation];
            string[] cols = ["L", "M", "N", "O", "P", "Q"];
            for (int i = 0; i < cols.Length; i++)
            {
                Assert.True(ws.Cell($"{cols[i]}{row}").HasFormula, $"{cols[i]}{row} should be a formula");
                Assert.Equal((double)(figures[i] / 1000m), Number(ws, $"{cols[i]}{row}"));
            }
        }
    }

    /// <summary>
    /// §13.3, as amended by PPDO-98: <b>each level sums the level directly below it</b>, never its whole
    /// block. ↩️ The office row was <c>SUM(M11:M14)</c> while the headings between were blank; with
    /// subtotals on them a block-wide range counts every activity three times.
    /// </summary>
    [Fact]
    public void Export_EachSubtotal_SumsOnlyTheLevelBelowIt()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.Equal("SUM(M13,M14)", ws.Cell("M12").FormulaA1);  // project ← its activities
        Assert.Equal("SUM(M12)",     ws.Cell("M11").FormulaA1);  // program ← its projects
        Assert.Equal("SUM(M11)",     ws.Cell("M10").FormulaA1);  // office  ← its programs
        // Office B: a synthetic project prints no heading, so its activity is the program's own child.
        Assert.Equal("SUM(M17)",     ws.Cell("M16").FormulaA1);
        Assert.Equal("SUM(M16)",     ws.Cell("M15").FormulaA1);
        Assert.Equal("SUM(M10,M15)", ws.Cell("M18").FormulaA1);  // TOTAL ← the office rows
        Assert.Equal("SUM(L13:N13)", ws.Cell("O13").FormulaA1);  // an activity's own Total column
    }

    /// <summary>§11 "Edited in Excel": change one MOOE cell and the Total, subtotal and TOTAL follow.</summary>
    [Fact]
    public void Export_EditingAnActivityCell_RecalculatesUpward()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        ws.Cell("M13").Value = 2000;
        wb.RecalculateAllFormulas();

        Assert.Equal(2002d, Number(ws, "O13"));
        Assert.Equal(2002d, Number(ws, "M10"));
        // Office B prints no MOOE (its one activity is CO), so the sheet TOTAL follows office A alone.
        Assert.Equal(2002d, Number(ws, "M18"));
    }

    /// <summary>Zero shows as "-" — the province's accounting format.</summary>
    [Fact]
    public void Export_Amounts_UseTheAccountingFormat()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.Equal(AipFormExcelService.AccountingFormat, ws.Cell("N13").Style.NumberFormat.Format);
        Assert.Equal(AipFormExcelService.AccountingFormat, ws.Cell("N18").Style.NumberFormat.Format);
    }

    /// <summary>§11 empty sector: preamble, header and a TOTAL of zero straight under the header.</summary>
    [Fact]
    public void Export_EmptySector_HasItsSheetWithAZeroTotal()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("OTHERS_FY2028");

        Assert.Equal("ANNUAL INVESTMENT PROGRAM (AIP) FY 2028", ws.Cell("A2").GetString());
        Assert.Equal("TOTAL", ws.Cell("A10").GetString());
        Assert.Equal(0d, Number(ws, "O10"));
        Assert.Equal(AipFormExcelService.AccountingFormat, ws.Cell("O10").Style.NumberFormat.Format);
    }

    // ── Note and page setup ───────────────────────────────────────────────────

    /// <summary>§13.4: the over-ceiling note, two rows under TOTAL, inside the print area.</summary>
    [Fact]
    public void Export_Note_SitsUnderTotalInsideThePrintArea()
    {
        using XLWorkbook wb = Open();
        IXLWorksheet ws = wb.Worksheet("GENERAL_FY2028");

        Assert.Equal(AipFormExcelService.UpliftNote, ws.Cell("A19").GetString());
        Assert.Equal("A19:R20", MergeOf(ws, "A19"));

        IXLRange printArea = Assert.Single(ws.PageSetup.PrintAreas);
        Assert.Equal("A1:R20", printArea.RangeAddress.ToStringRelative());
    }

    /// <summary>§13.5: landscape, one page wide, the two header rows repeating.</summary>
    [Fact]
    public void Export_PageSetup_IsLandscapeOnePageWideWithRepeatingHeader()
    {
        using XLWorkbook wb = Open();

        Assert.All(wb.Worksheets, ws =>
        {
            Assert.Equal(XLPageOrientation.Landscape, ws.PageSetup.PageOrientation);
            Assert.Equal(1, ws.PageSetup.PagesWide);
            Assert.Equal(0, ws.PageSetup.PagesTall);
            Assert.Equal(8, ws.PageSetup.FirstRowToRepeatAtTop);
            Assert.Equal(9, ws.PageSetup.LastRowToRepeatAtTop);
        });
    }

    /// <summary>On screen: panes frozen below the header (row 9), no columns frozen.</summary>
    [Fact]
    public void Export_SheetView_FreezesTheHeaderRows()
    {
        using XLWorkbook wb = Open();

        Assert.All(wb.Worksheets, ws =>
        {
            Assert.Equal(9, ws.SheetView.SplitRow);
            Assert.Equal(0, ws.SheetView.SplitColumn);
        });
    }
}
