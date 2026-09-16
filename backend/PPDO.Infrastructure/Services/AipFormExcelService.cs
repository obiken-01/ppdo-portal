using System.Globalization;
using ClosedXML.Excel;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IAipFormExcelService"/> — the FY2028+ AIP as the province's Annex B workbook
/// (V18-60 / PPDO-84, <c>docs/v1.8/AIP_Form_Spec.md</c> Part II §13). Built programmatically from the
/// style catalogue below, the <see cref="PpmpReportExcelService"/> way; the layout is copied from the
/// province's FY2027 file (<c>AIP_2027_PGOM_Test.xlsm</c>).
///
/// <para>
/// ⚠️ <b>Unlike the WFP and PPMP exports, totals here are <c>SUM</c> formulas</b> (decision 4) — the
/// province's own construction, so a figure PPDO edits before printing carries up the sheet. Activity
/// amounts are values. The tests pin every formula's result to the builder's figure.
/// </para>
///
/// Column map (1-based) — the description column IS the level (§4):
///   A  Ref code      F  eSRE (no DBM #)   K  Funding source (7)   P  CC adaptation (12)
///   B  Office        G  Office (3)        L  PS (8)               Q  CC mitigation (13)
///   C  Program       H  Start (4)         M  MOOE (9)             R  CC typology (14)
///   D  Project       I  Completion (5)    N  CO (10)
///   E  Activity      J  Outputs (6)       O  Total (11) = SUM(L:N)
/// </summary>
public sealed class AipFormExcelService : IAipFormExcelService
{
    // ── Column indices (1-based) ───────────────────────────────────────────────
    private const int ColRef       = 1;  // A
    private const int ColOffice    = 2;  // B
    private const int ColProgram   = 3;  // C
    private const int ColProject   = 4;  // D
    private const int ColActivity  = 5;  // E
    private const int ColEsre      = 6;  // F
    private const int ColImpl      = 7;  // G
    private const int ColStart     = 8;  // H
    private const int ColEnd       = 9;  // I
    private const int ColOutputs   = 10; // J
    private const int ColFunding   = 11; // K
    private const int ColPs        = 12; // L
    private const int ColMooe      = 13; // M
    private const int ColCo        = 14; // N
    private const int ColTotal     = 15; // O
    private const int ColCcAdapt   = 16; // P
    private const int ColCcMitig   = 17; // Q
    private const int ColTypology  = 18; // R
    private const int LastCol      = ColTypology;

    private const int HeaderTop    = 8;
    private const int HeaderBottom = 9;
    private const int FirstBodyRow = 10;

    /// <summary>The six amount columns L–Q, in <see cref="AmountsOf"/> order.</summary>
    private static readonly int[] AmountCols = [ColPs, ColMooe, ColCo, ColTotal, ColCcAdapt, ColCcMitig];

    // ── Style catalogue ────────────────────────────────────────────────────────
    private const string FontName = "Arial Narrow";
    private const double BodyFontSize = 10;
    private const double TitleFontSize = 11;
    private static readonly XLColor ClrOffice = XLColor.FromHtml("#E2EFDA");
    private static readonly XLColor ClrHeader = XLColor.FromHtml("#F2F2F2");

    /// <summary>The province's accounting format — zero shows as "-".</summary>
    public const string AccountingFormat = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)";

    /// <summary>
    /// ⚠️ <b>Draft wording, PPDC to agree</b> (§6.2, §13.4). The one place it lives — replacing it is a
    /// one-line change. Printed under TOTAL on every sheet.
    /// </summary>
    public const string UpliftNote =
        "Note: Maintenance and Other Operating Expenses (MOOE) and Capital Outlay (CO) include a 30% " +
        "adjustment. Budget ceilings are checked against the amounts before the adjustment, so an office's " +
        "printed MOOE and CO may exceed its ceiling by up to 30%. Amounts are rounded up to the nearest " +
        "thousand pesos, and every total is the sum of the rounded figures.";

    // index = column number (1-based); 0 = unused slot 0. From the province's file (§13.3).
    private static readonly double[] ColWidths =
    [
        0, 12.4, 1.6, 2.6, 4.2, 38, 9.9, 11.2, 10.2, 10.2, 18.2, 9.8, 13.6, 13.6, 13.6, 13.6, 9.9, 9.9, 8.8,
    ];

    public byte[] Export(AipFormWorkbookDto workbook)
    {
        using XLWorkbook wb = new();

        foreach (AipFormWorkbookSheetDto sheet in workbook.Sheets)
            WriteSheet(wb, workbook, sheet);

        using MemoryStream ms = new();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void WriteSheet(XLWorkbook wb, AipFormWorkbookDto workbook, AipFormWorkbookSheetDto sheet)
    {
        IXLWorksheet ws = wb.AddWorksheet($"{sheet.Sector}_FY{workbook.FiscalYear}");

        ws.Style.Font.SetFontName(FontName).Font.SetFontSize(BodyFontSize);
        ws.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        for (int c = 1; c <= LastCol; c++)
            ws.Column(c).Width = ColWidths[c];

        WritePreamble(ws, workbook);
        WriteHeader(ws);

        int row = FirstBodyRow;
        List<int> officeRows = [];
        int officeRow = 0;

        // ⚠️ **Each level SUMs the level directly below it, never its whole block** (PPDO-98). The
        // office row used to sum every row beneath it, which was exact only while program and project
        // headings were blank. Now that a heading carries its own subtotal, a block-wide sum would
        // count each activity three times, so the writer tracks each open heading's direct children.
        List<int> programRows        = [];  // the current office's program headings
        List<int> programChildRows   = [];  // the current program's project headings, plus any activity
                                           // printed straight under it (a synthetic project, RAL-108)
        List<int> projectActivityRows = []; // the current project's activities
        int programRow = 0;
        int projectRow = 0;

        void CloseProject()
        {
            if (projectRow == 0) return;
            // ⚠️ A project with nothing costed under it prints BLANK, so it must also drop out of its
            // program's sum — summing a blank cell would put a ₱0 on the program while the project
            // itself stayed empty, and the grid (which reads the builder's null) would disagree.
            if (projectActivityRows.Count == 0) programChildRows.Remove(projectRow);
            else SumCells(ws, projectRow, projectActivityRows);
            projectRow = 0;
            projectActivityRows.Clear();
        }

        void CloseProgram()
        {
            CloseProject();
            if (programRow == 0) return;
            if (programChildRows.Count == 0) programRows.Remove(programRow);
            else SumCells(ws, programRow, programChildRows);
            programRow = 0;
            programChildRows.Clear();
        }

        void CloseOffice()
        {
            CloseProgram();
            if (officeRow == 0) return;
            // ⚠️ An office row is a figure even with nothing printed beneath it: it is a submission, and
            // reads as ₱0. A heading in the same position was simply never costed, which is why it stays
            // blank. (The builder drops such an office before it reaches here; the writer still answers.)
            if (programRows.Count == 0)
                foreach (int col in AmountCols) ws.Cell(officeRow, col).Value = 0;
            else
                SumCells(ws, officeRow, programRows);
            programRows.Clear();
        }

        foreach (AipConsolidatedRowDto line in sheet.Rows)
        {
            switch (line.Kind)
            {
                case AipFormRowBuilder.OfficeRow:
                    CloseOffice();
                    officeRow = row;
                    officeRows.Add(row);
                    WriteOffice(ws, row, line);
                    break;
                case AipFormRowBuilder.ProgramRow:
                    CloseProgram();
                    programRow = row;
                    programRows.Add(row);
                    WriteHeading(ws, row, line, ColProgram, s => s.Font.SetBold(true));
                    break;
                case AipFormRowBuilder.ProjectRow:
                    CloseProject();
                    projectRow = row;
                    programChildRows.Add(row);
                    WriteHeading(ws, row, line, ColProject, s => s.Font.SetBold(true).Font.SetItalic(true));
                    break;
                case AipFormRowBuilder.ActivityRow:
                    // An activity with no project heading open belongs to a synthetic project, so it
                    // is the program's own child.
                    (projectRow == 0 ? programChildRows : projectActivityRows).Add(row);
                    WriteActivity(ws, row, line);
                    break;
            }
            // Every level's code wraps in A — a program or project code is longer than the column,
            // and clipped it reads as a different code. Centred, as the province's file has it.
            ws.Cell(row, ColRef).Style
                .Alignment.SetWrapText(true)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            row++;
        }
        CloseOffice();

        int totalRow = row;
        WriteTotal(ws, totalRow, officeRows);
        StyleGrid(ws, totalRow);

        int noteRow = totalRow + 1;
        ws.Range(noteRow, ColRef, noteRow + 1, LastCol).Merge();
        ws.Cell(noteRow, ColRef).Value = UpliftNote;
        ws.Cell(noteRow, ColRef).Style
            .Font.SetItalic(true)
            .Alignment.SetWrapText(true)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Top);

        SetUpPage(ws, lastRow: noteRow + 1);
    }

    // ── Preamble — rows 1–7 (§13.1) ───────────────────────────────────────────

    private static void WritePreamble(IXLWorksheet ws, AipFormWorkbookDto workbook)
    {
        string asOf = workbook.AsOf.ToString("MMMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant();

        MergedTitle(ws, 1, "Annex B", XLAlignmentHorizontalValues.Right);
        MergedTitle(ws, 2, $"ANNUAL INVESTMENT PROGRAM (AIP) FY {workbook.FiscalYear}", XLAlignmentHorizontalValues.Center);
        MergedTitle(ws, 3, "By Program/Project/Activity by Sector", XLAlignmentHorizontalValues.Center);
        MergedTitle(ws, 4, $"As of {asOf}", XLAlignmentHorizontalValues.Center);

        // ⚠️ Decision 1: while any office is missing the totals are partial, and the form says so.
        if (workbook.SubmittedOffices < workbook.TotalOffices)
        {
            ws.Range(5, ColRef, 5, LastCol).Merge();
            ws.Cell(5, ColRef).Value =
                $"Includes {workbook.SubmittedOffices} of {workbook.TotalOffices} offices — " +
                "offices still being prepared or reviewed are not in these totals.";
            ws.Cell(5, ColRef).Style
                .Font.SetItalic(true)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }
        else
        {
            ws.Row(5).Height = 6;
        }

        ws.Cell(6, ColRef).Value = "Province/City/Municipality/Barangay: OCCIDENTAL MINDORO";
        ws.Cell(6, ColRef).Style.Font.SetBold(true).Font.SetFontSize(TitleFontSize);
        ws.Row(7).Height = 6;
    }

    private static void MergedTitle(IXLWorksheet ws, int row, string text, XLAlignmentHorizontalValues align)
    {
        ws.Range(row, ColRef, row, LastCol).Merge();
        ws.Cell(row, ColRef).Value = text;
        ws.Cell(row, ColRef).Style
            .Font.SetBold(true)
            .Font.SetFontSize(TitleFontSize)
            .Alignment.SetHorizontal(align);
    }

    // ── Header — rows 8–9 (§13.2) ─────────────────────────────────────────────

    private static void WriteHeader(IXLWorksheet ws)
    {
        // Spans first, so every label lands in its merge's top-left cell.
        HeaderSpan(ws, ColRef, ColRef, rows: 2, "AIP Reference Code\n(1)");
        HeaderSpan(ws, ColOffice, ColActivity, rows: 2, "Program/Project/Activity Description\n(2)");
        // ⚠️ No DBM number: eSRE is a provincial insertion, not an Annex B column (§3).
        HeaderSpan(ws, ColEsre, ColEsre, rows: 2, "eSRE Code");
        HeaderSpan(ws, ColImpl, ColImpl, rows: 2, "Implementing Office/ Department\n(3)");
        HeaderSpan(ws, ColStart, ColEnd, rows: 1, "Schedule of Implementation");
        HeaderSpan(ws, ColOutputs, ColOutputs, rows: 2, "Expected Outputs\n(6)");
        HeaderSpan(ws, ColFunding, ColFunding, rows: 2, "Funding Source\n(7)");
        // ⚠️ Units stated on the AMOUNT block — the FY2027 form did not (§6 #5).
        HeaderSpan(ws, ColPs, ColTotal, rows: 1, "AMOUNT (In Thousand Pesos)");
        HeaderSpan(ws, ColCcAdapt, ColTypology, rows: 1, "AMOUNT of Climate Change Expenditure (In Thousand Pesos)");

        ws.Cell(HeaderBottom, ColStart).Value    = "Start Date\n(4)";
        ws.Cell(HeaderBottom, ColEnd).Value      = "Completion Date\n(5)";
        ws.Cell(HeaderBottom, ColPs).Value       = "Personal Services (PS)\n(8)";
        ws.Cell(HeaderBottom, ColMooe).Value     = "Maintenance and Other Operating Expenses (MOOE)\n(9)";
        ws.Cell(HeaderBottom, ColCo).Value       = "Capital Outlay (CO)\n(10)";
        ws.Cell(HeaderBottom, ColTotal).Value    = "Total\n(11) = (8+9+10)";
        ws.Cell(HeaderBottom, ColCcAdapt).Value  = "Climate Change Adaptation\n(12)";
        ws.Cell(HeaderBottom, ColCcMitig).Value  = "Climate Change Mitigation\n(13)";
        ws.Cell(HeaderBottom, ColTypology).Value = "CC Typology Code\n(14)";

        ws.Range(HeaderTop, ColRef, HeaderBottom, LastCol).Style
            .Font.SetBold(true)
            .Fill.SetBackgroundColor(ClrHeader)
            .Alignment.SetWrapText(true)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Row(HeaderTop).Height = 28;
        // Tall enough for the longest label — "Maintenance and Other Operating Expenses (MOOE) (9)" in
        // column M wraps to five lines and was clipped at 56.
        ws.Row(HeaderBottom).Height = 80;
    }

    private static void HeaderSpan(IXLWorksheet ws, int fromCol, int toCol, int rows, string label)
    {
        IXLRange span = ws.Range(HeaderTop, fromCol, HeaderTop + rows - 1, toCol);
        if (rows > 1 || fromCol != toCol) span.Merge();
        ws.Cell(HeaderTop, fromCol).Value = label;
    }

    // ── Body — from row 10 (§13.3) ────────────────────────────────────────────

    private static void WriteOffice(IXLWorksheet ws, int row, AipConsolidatedRowDto line)
    {
        ws.Cell(row, ColRef).Value = line.RefCode;
        ws.Cell(row, ColOffice).Value = line.Name;
        ws.Range(row, ColRef, row, LastCol).Style
            .Font.SetBold(true)
            .Fill.SetBackgroundColor(ClrOffice);
        SetAmountFormat(ws, row);
    }

    /// <summary>
    /// Writes a subtotal row's amount cells as a <c>SUM</c> over the named rows — the level directly
    /// below it (PPDO-98), never a whole block.
    /// </summary>
    /// <param name="childRows">
    /// The rows this one totals. ⚠️ Named individually rather than as a range: a program's children are
    /// its project headings and they are not contiguous, and a range would also swallow the activity
    /// rows those headings already count.
    /// </param>
    private static void SumCells(IXLWorksheet ws, int row, IReadOnlyList<int> childRows)
    {
        foreach (int col in AmountCols)
            ws.Cell(row, col).FormulaA1 = $"SUM({string.Join(",", childRows.Select(r => Ref(col, r)))})";
    }

    private static void WriteHeading(
        IXLWorksheet ws, int row, AipConsolidatedRowDto line, int nameCol, Action<IXLStyle> style)
    {
        ws.Cell(row, ColRef).Value = line.RefCode;
        ws.Range(row, nameCol, row, ColActivity).Merge();
        ws.Cell(row, nameCol).Value = line.Name;
        ws.Cell(row, nameCol).Style.Alignment.SetWrapText(true);
        // ⚠️ Styled and formatted out to the last column since PPDO-98: the row carries a subtotal
        // now, and a bold heading with plain unformatted figures beside it reads as two rows.
        style(ws.Range(row, ColRef, row, LastCol).Style);
        SetAmountFormat(ws, row);
    }

    private static void WriteActivity(IXLWorksheet ws, int row, AipConsolidatedRowDto line)
    {
        ws.Cell(row, ColRef).Value      = line.RefCode;
        ws.Cell(row, ColActivity).Value = line.Name;
        ws.Cell(row, ColEsre).Value     = line.EsreCode ?? string.Empty;
        ws.Cell(row, ColImpl).Value     = line.ImplementingOffice ?? string.Empty;
        ws.Cell(row, ColStart).Value    = line.StartDate ?? string.Empty;
        ws.Cell(row, ColEnd).Value      = line.EndDate ?? string.Empty;
        ws.Cell(row, ColOutputs).Value  = line.ExpectedOutputs ?? string.Empty;
        ws.Cell(row, ColFunding).Value  = line.FundingSource ?? string.Empty;
        ws.Cell(row, ColTypology).Value = line.CcTypologyCode ?? string.Empty;

        AipPrintedAmountsDto amounts = line.Amounts ?? AipPrintedFigures.Zero;
        decimal[] figures = AmountsOf(amounts);
        for (int i = 0; i < AmountCols.Length; i++)
        {
            if (AmountCols[i] == ColTotal) continue;
            // ⚠️ Decision 6: thousands — ₱1,301,000 is written 1301. Already rounded; never again.
            ws.Cell(row, AmountCols[i]).Value = figures[i] / 1000m;
        }
        ws.Cell(row, ColTotal).FormulaA1 = $"SUM({Ref(ColPs, row)}:{Ref(ColCo, row)})";

        foreach (int col in new[] { ColRef, ColActivity, ColImpl, ColOutputs })
            ws.Cell(row, col).Style.Alignment.SetWrapText(true);
        SetAmountFormat(ws, row);
    }

    private static void WriteTotal(IXLWorksheet ws, int row, IReadOnlyList<int> officeRows)
    {
        ws.Cell(row, ColRef).Value = "TOTAL";

        foreach (int col in AmountCols)
        {
            if (officeRows.Count == 0)
                ws.Cell(row, col).Value = 0;
            else
                ws.Cell(row, col).FormulaA1 = $"SUM({string.Join(",", officeRows.Select(r => Ref(col, r)))})";
        }

        ws.Range(row, ColRef, row, LastCol).Style
            .Font.SetBold(true)
            .Font.SetFontSize(TitleFontSize)
            .Border.SetTopBorder(XLBorderStyleValues.Medium);
        SetAmountFormat(ws, row);
    }

    /// <summary>
    /// Thin borders over the header and body. ⚠️ The four description columns B–E are one cell to the
    /// eye — the level is which of them holds the text — so no vertical line is drawn between them in
    /// the body.
    /// </summary>
    private static void StyleGrid(IXLWorksheet ws, int totalRow)
    {
        ws.Range(HeaderTop, ColRef, totalRow, LastCol).Style
            .Border.SetInsideBorder(XLBorderStyleValues.Thin)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        // ⚠️ One column at a time: on a multi-column range ClosedXML's Right/LeftBorder set only the
        // range's outer edge, which is how the first build left the lines in. TOTAL included.
        for (int col = ColOffice; col < ColActivity; col++)
        {
            ws.Range(FirstBodyRow, col, totalRow, col).Style.Border.SetRightBorder(XLBorderStyleValues.None);
            ws.Range(FirstBodyRow, col + 1, totalRow, col + 1).Style.Border.SetLeftBorder(XLBorderStyleValues.None);
        }

        // The medium rule above TOTAL, restored after the thin grid.
        ws.Range(totalRow, ColRef, totalRow, LastCol).Style.Border.SetTopBorder(XLBorderStyleValues.Medium);
    }

    // ── Page setup (§13.5) ────────────────────────────────────────────────────

    private static void SetUpPage(IXLWorksheet ws, int lastRow)
    {
        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.SetLeft(0.04).SetRight(0.04).SetTop(0.28).SetBottom(0.28);
        ws.PageSetup.SetRowsToRepeatAtTop(HeaderTop, HeaderBottom);
        // On screen as on paper: the header stays put while a reader scrolls hundreds of rows.
        ws.SheetView.FreezeRows(HeaderBottom);
        ws.PageSetup.PrintAreas.Add(1, ColRef, lastRow, LastCol);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static decimal[] AmountsOf(AipPrintedAmountsDto a)
        => [a.Ps, a.Mooe, a.Co, a.Total, a.CcAdaptation, a.CcMitigation];

    private static void SetAmountFormat(IXLWorksheet ws, int row)
        => ws.Range(row, ColPs, row, ColCcMitig).Style.NumberFormat.SetFormat(AccountingFormat);

    private static string Ref(int col, int row) => $"{XLHelper.GetColumnLetterFromNumber(col)}{row}";
}
