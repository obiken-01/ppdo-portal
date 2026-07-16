using ClosedXML.Excel;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IWfpReportExcelService"/> — populates a workbook shaped like the PBO's
/// "WFP FINAL" form (v1.4.4, RAL-159) directly via ClosedXML, rather than cloning rows out of a
/// bundled copy of the PBO form. That template-cloning approach was considered and rejected: the
/// only real-world copy of the form available (WFP-NEW.xlsx) is a filled sample with
/// inconsistent hand-touched cell borders, not a blank template, so cloning its rows would carry
/// that noise into every export. Instead, styling is driven by the constants below (extracted
/// from inspecting WFP-NEW.xlsx) and vertical merges are computed from the actual number of rows
/// each block emits — see docs/v1.4.4/WFP_Excel_Export_Assessment.md for the full column map and
/// the merge/style findings behind this design.
///
/// Column layout (1-based, matches the source form's B–V columns exactly):
///   B  AIP REF CODE            L  NATURE OF IMPLEMENTATION
///   C  PROGRAM title           M  ACCOUNT CODE / PS-MOOE-CO header / SUB-TOTAL label
///   D  PROJECT title           N  OBJECT OF EXPENDITURE
///   E  ACTIVITY title          O  TOTAL APPROPRIATION
///   F  Resources Needed (blank — F–K out of scope for v1.4.4, see assessment §4)
///   G  Responsible Person/Unit (blank)      P  RESERVED
///   H  Success Indicator (blank)            Q  NET APPROPRIATION
///   I  Means of Verification (blank)        R–U  Q1–Q4
///   J  Outcome Indicator (blank)            V  AMOUNT TO BE RELEASED
///   K  Target Beneficiaries (blank)
///
/// One worksheet per <see cref="WfpReportFundSourceDto"/> (the source form's "block repeats per
/// fund" pattern — RAL-159 §5, decided one-sheet-per-fund over stacked blocks on one sheet).
/// Every amount is written as a literal value from the DTO — the source form's VLOOKUP/SUM
/// formulas are never reproduced (confirmed with Ralph 2026-07-16: overwrite outright).
/// </summary>
public sealed class WfpReportExcelService : IWfpReportExcelService
{
    // ── Column indices ───────────────────────────────────────────────────────
    private const int ColRef      = 2;  // B
    private const int ColProgram  = 3;  // C
    private const int ColProject  = 4;  // D
    private const int ColActivity = 5;  // E
    private const int ColFkFirst  = 6;  // F
    private const int ColFkLast   = 11; // K
    private const int ColNature   = 12; // L
    private const int ColAccount  = 13; // M
    private const int ColObject   = 14; // N
    private const int ColTotal    = 15; // O
    private const int ColReserved = 16; // P
    private const int ColNet      = 17; // Q
    private const int ColQ1       = 18; // R
    private const int ColQ4       = 21; // U
    private const int ColRelease  = 22; // V
    private const int LastCol     = 22; // V — hidden check column W is not written

    // ── Style catalog (extracted from WFP-NEW.xlsx inspection, 2026-07-16) ─────
    private static readonly XLColor ClrColHeader   = XLColor.FromHtml("#A8D08D");
    private static readonly XLColor ClrSubTotal    = XLColor.FromHtml("#92D050");
    private static readonly XLColor ClrActivityTot = XLColor.FromHtml("#FFFF00");
    private static readonly XLColor ClrProgramTot  = XLColor.FromHtml("#FFC000");
    private static readonly XLColor ClrBreakdown   = XLColor.FromHtml("#C5E0B3");
    private const string FontName      = "Arial Narrow";
    private const double FontSize      = 13;
    private const string AccountingFmt = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)";

    // index = column number, 0 = unused
    private static readonly double[] ColWidths =
    {
        0, 0, 37.8, 1.0, 20, 31.5, 12.5, 15.2, 14.3, 13.8, 18.7, 13.8,
        20, 20, 45.8, 17.5, 17.7, 17.5, 15.7, 15.7, 15.7, 15.7, 18.2,
    };

    public byte[] Export(WfpReportDto report)
    {
        using XLWorkbook wb = new();

        foreach (WfpReportFundSourceDto fund in report.FundSourceReports)
            WriteFundSheet(wb, report, fund);

        if (report.FundSourceReports.Count == 0)
            WriteFundSheet(wb, report, new WfpReportFundSourceDto(
                "GENERAL FUND", Array.Empty<WfpReportFunctionBandSectionDto>(),
                new WfpReportBreakdownDto(
                    WfpReportAmountsDto.Zero, WfpReportAmountsDto.Zero, WfpReportAmountsDto.Zero,
                    WfpReportAmountsDto.Zero, WfpReportAmountsDto.Zero, WfpReportAmountsDto.Zero)));

        using MemoryStream ms = new();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private void WriteFundSheet(XLWorkbook wb, WfpReportDto report, WfpReportFundSourceDto fund)
    {
        IXLWorksheet ws = wb.AddWorksheet(SafeSheetName(fund.FundSourceName, wb));

        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        ws.PageSetup.PrintAreas.Add(1, 1, 200, LastCol);

        for (int c = ColRef; c <= LastCol; c++)
            if (ColWidths[c] > 0) ws.Column(c).Width = ColWidths[c];

        ws.Style.Font.SetFontName(FontName);
        ws.Style.Font.SetFontSize(FontSize);

        int row = 2;

        MergeH(ws, row, ColRef, ColNet, $"WORK AND FINANCIAL PLAN FY {report.FiscalYear}",
            s => s.Font.SetBold(true).Font.SetFontSize(16)
                  .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        MergeH(ws, row, ColRef, ColNet, $"FY {report.FiscalYear}",
            s => s.Font.SetBold(true).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        ws.Cell(row, ColRef).Value = "DEPARTMENT/OFFICE:";
        ws.Cell(row, ColRef).Style.Font.SetBold(true);
        MergeH(ws, row, ColActivity, ColNet, report.OfficeName, s => { });
        row++;

        ws.Cell(row, ColRef).Value = "SOURCE OF FUND:";
        ws.Cell(row, ColRef).Style.Font.SetBold(true);
        MergeH(ws, row, ColActivity, ColNet, fund.FundSourceName.ToUpperInvariant(),
            s => s.Font.SetBold(true));
        row++;

        ws.Cell(row, ColReserved).Value = $"Equiv. to {report.ReserveRate:P0} of Operational Expenses";
        ws.Cell(row, ColReserved).Style.Font.SetItalic(true).Font.SetFontSize(9);
        row++;

        WriteColumnHeaders(ws, row);
        row += 2;
        // No freeze panes — intentionally dropped, Ralph confirmed 2026-07-16.

        foreach (WfpReportFunctionBandSectionDto section in fund.Sections)
            row = WriteFunctionBand(ws, section, row);

        row = WriteBreakdown(ws, fund.Breakdown, row);

        if (row > 9)
            ws.Range(9, ColRef, row - 1, ColRelease).Style
                .Border.SetInsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin);
    }

    private void WriteColumnHeaders(IXLWorksheet ws, int row)
    {
        (int col, string label)[] singleHeaders =
        {
            (ColRef, "AIP REF CODE"),
            (ColFkFirst, "RESOURCES NEEDED"),
            (ColFkFirst + 1, "RESPONSIBLE PERSON/UNIT"),
            (ColFkFirst + 2, "SUCCESS INDICATOR"),
            (ColFkFirst + 3, "MEANS OF VERIFICATION"),
            (ColFkFirst + 4, "OUTCOME INDICATOR"),
            (ColFkLast, "TARGET BENEFICIARIES"),
            (ColNature, "NATURE OF IMPLEMENTATION"),
            (ColAccount, "ACCOUNT CODE"),
            (ColObject, "OBJECT OF EXPENDITURE"),
            (ColTotal, "TOTAL APPROPRIATION"),
            (ColReserved, "RESERVED"),
            (ColNet, "NET APPROPRIATION"),
            (ColRelease, "AMOUNT TO BE RELEASED"),
        };

        foreach ((int col, string label) in singleHeaders)
        {
            ws.Range(row, col, row + 1, col).Merge();
            ws.Cell(row, col).Value = label;
        }

        ws.Range(row, ColProgram, row + 1, ColActivity).Merge();
        ws.Cell(row, ColProgram).Value = "PROGRAMS, PROJECTS AND ACTIVITIES";

        ws.Range(row, ColQ1, row, ColQ4).Merge();
        ws.Cell(row, ColQ1).Value = "TIME FRAME";
        string[] quarters = { "1ST QUARTER", "2ND QUARTER", "3RD QUARTER", "4TH QUARTER" };
        for (int i = 0; i < 4; i++)
            ws.Cell(row + 1, ColQ1 + i).Value = quarters[i];

        ws.Range(row, ColRef, row + 1, ColRelease).Style
            .Fill.SetBackgroundColor(ClrColHeader)
            .Font.SetBold(true)
            .Alignment.SetWrapText(true)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
            .Border.SetInsideBorder(XLBorderStyleValues.Thin);

        ws.Row(row).Height = 30;
        ws.Row(row + 1).Height = 20;
    }

    private int WriteFunctionBand(IXLWorksheet ws, WfpReportFunctionBandSectionDto section, int row)
    {
        if (section.Programs.Count == 0) return row;

        MergeH(ws, row, ColRef, ColRelease, section.FunctionBandLabel,
            s => s.Font.SetBold(true).Font.SetItalic(true));
        row++;

        foreach (WfpReportProgramDto program in section.Programs)
            row = WriteProgram(ws, program, row);

        return row;
    }

    private int WriteProgram(IXLWorksheet ws, WfpReportProgramDto program, int row)
    {
        ws.Cell(row, ColRef).Value = program.RefCode;
        MergeH(ws, row, ColProgram, ColRelease, program.Name, s => { });
        ws.Range(row, ColRef, row, ColRelease).Style.Font.SetBold(true);
        row++;

        foreach (WfpReportProjectDto project in program.Projects)
            row = WriteProject(ws, project, row);

        WriteTotalRow(ws, row, "PROGRAM GRAND TOTAL", program.GrandTotal, ClrProgramTot);
        row++;

        return row;
    }

    private int WriteProject(IXLWorksheet ws, WfpReportProjectDto project, int row)
    {
        ws.Cell(row, ColRef).Value = project.RefCode;
        MergeH(ws, row, ColProject, ColRelease, project.Name, s => { });
        ws.Range(row, ColRef, row, ColRelease).Style.Font.SetBold(true);
        row++;

        foreach (WfpReportActivityDto activity in project.Activities)
            row = WriteActivity(ws, activity, row);

        WriteTotalRow(ws, row, "PROJECT GRAND TOTAL", project.GrandTotal, ClrProgramTot);
        row++;

        return row;
    }

    private int WriteActivity(IXLWorksheet ws, WfpReportActivityDto activity, int row)
    {
        int blockStart = row;

        foreach (WfpReportExpenseClassGroupDto group in activity.ExpenseClasses)
        {
            ws.Cell(row, ColAccount).Value = group.ExpenseClassLabel;
            ws.Cell(row, ColAccount).Style.Font.SetBold(true)
                .Border.SetTopBorder(XLBorderStyleValues.Thin);
            row++;

            foreach (WfpReportRowDto line in group.Rows)
            {
                ws.Cell(row, ColNature).Value  = line.Nature;
                ws.Cell(row, ColAccount).Value = line.AccountNumber;
                ws.Cell(row, ColObject).Value  = line.AccountTitle;
                WriteAmounts(ws, row, line.Amounts);
                row++;
            }

            WriteTotalRow(ws, row, "SUB-TOTAL", group.SubTotal, ClrSubTotal, col: ColAccount);
            row++;
        }

        int blockEnd = row - 1;

        if (blockEnd >= blockStart)
        {
            ws.Cell(blockStart, ColRef).Value = activity.RefCode;
            ws.Cell(blockStart, ColActivity).Value = activity.Name;
            VMerge(ws, blockStart, blockEnd, ColRef);
            VMerge(ws, blockStart, blockEnd, ColActivity);
            for (int c = ColFkFirst; c <= ColFkLast; c++)
                VMerge(ws, blockStart, blockEnd, c); // blank — F–K out of scope for v1.4.4
            ws.Range(blockStart, ColRef, blockEnd, ColActivity).Style
                .Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        }

        WriteTotalRow(ws, row, "ACTIVITY GRAND TOTAL", activity.GrandTotal, ClrActivityTot);
        row++;

        return row;
    }

    private int WriteBreakdown(IXLWorksheet ws, WfpReportBreakdownDto breakdown, int row)
    {
        (string label, WfpReportAmountsDto amounts, XLColor fill)[] rows =
        {
            ("TOTAL - PERSONAL SERVICES", breakdown.PersonalServices, ClrBreakdown),
            ("TOTAL - MOOE (Excluding Creation)", breakdown.MooeExcludingCreation, ClrBreakdown),
            ("TOTAL - CAPITAL OUTLAY", breakdown.CapitalOutlay, ClrBreakdown),
            ("TOTAL - PERSONAL SERVICES CREATION", breakdown.PersonalServicesCreation, ClrBreakdown),
            ("TOTAL - MOOE - CREATION", breakdown.MooeCreation, ClrBreakdown),
            ("GRAND-TOTAL", breakdown.GrandTotal, ClrActivityTot),
        };

        foreach ((string label, WfpReportAmountsDto amounts, XLColor fill) in rows)
        {
            WriteTotalRow(ws, row, label, amounts, fill);
            row++;
        }

        return row;
    }

    private void WriteTotalRow(
        IXLWorksheet ws, int row, string label, WfpReportAmountsDto amounts, XLColor fill,
        int col = ColAccount)
    {
        ws.Cell(row, col).Value = label;
        WriteAmounts(ws, row, amounts);
        ws.Range(row, ColRef, row, ColRelease).Style
            .Font.SetBold(true)
            .Fill.SetBackgroundColor(fill)
            .Border.SetTopBorder(XLBorderStyleValues.Thin)
            .Border.SetBottomBorder(XLBorderStyleValues.Thin);
    }

    private static void WriteAmounts(IXLWorksheet ws, int row, WfpReportAmountsDto a)
    {
        ws.Cell(row, ColTotal).Value    = a.TotalAppropriation;
        ws.Cell(row, ColReserved).Value = a.Reserved;
        ws.Cell(row, ColNet).Value      = a.NetAppropriation;
        ws.Cell(row, ColQ1).Value       = a.Q1;
        ws.Cell(row, ColQ1 + 1).Value   = a.Q2;
        ws.Cell(row, ColQ1 + 2).Value   = a.Q3;
        ws.Cell(row, ColQ4).Value       = a.Q4;
        ws.Cell(row, ColRelease).Value  = a.AmountToBeReleased;
        ws.Range(row, ColTotal, row, ColRelease).Style.NumberFormat.SetFormat(AccountingFmt);
    }

    private static void MergeH(IXLWorksheet ws, int row, int colFrom, int colTo, string value,
        Action<IXLStyle> style)
    {
        ws.Range(row, colFrom, row, colTo).Merge();
        ws.Cell(row, colFrom).Value = value;
        style(ws.Cell(row, colFrom).Style);
    }

    private static void VMerge(IXLWorksheet ws, int rowFrom, int rowTo, int col)
    {
        if (rowTo > rowFrom)
            ws.Range(rowFrom, col, rowTo, col).Merge();
    }

    private static string SafeSheetName(string fundSourceName, XLWorkbook wb)
    {
        string name = new string(fundSourceName.Where(c => !"\\/*?:[]".Contains(c)).ToArray());
        if (name.Length > 31) name = name[..31];
        if (name.Length == 0) name = "Fund";

        string candidate = name;
        int suffix = 2;
        while (wb.Worksheets.Contains(candidate))
            candidate = $"{name[..Math.Min(name.Length, 28)]} {suffix++}";

        return candidate;
    }
}
