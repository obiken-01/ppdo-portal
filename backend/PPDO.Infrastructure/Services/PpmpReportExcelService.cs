using ClosedXML.Excel;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Implements <see cref="IPpmpReportExcelService"/> — populates a workbook shaped like the
/// province's own PPMP working form (<c>ppmp admin2027.xlsx</c>) directly via ClosedXML, rather
/// than cloning rows out of a bundled copy. The province's files are filled samples with
/// hand-touched formatting, not blank templates, so styling is driven by the constants below and
/// every value is written as a literal from the DTO — never a formula (same design as v1.4.4's
/// <see cref="WfpReportExcelService"/>; rationale in docs/v1.5/PPMP_Report_Findings.md §4.1/§8).
///
/// Column layout (1-based, item-grained — Mode of Procurement dropped per Q12):
///   A  Item No.            G  Unit Price          M  2nd Amount
///   B  AIP Reference Code  H  Qty                 N  3rd Qty
///   C  Description         I  Est. Budget         O  3rd Amount
///   D  Stock Card No.      J  1st Qty             P  4th Qty
///   E  Short Category      K  1st Amount          Q  4th Amount
///   F  Unit                L  2nd Qty             R  Total
///
/// One worksheet per <see cref="PpmpReportFundSourceDto"/> — the province files one PPMP per fund
/// source ("Charged to: …"), matching the WFP export's one-sheet-per-fund convention.
/// </summary>
public sealed class PpmpReportExcelService : IPpmpReportExcelService
{
    // ── Column indices (1-based) ───────────────────────────────────────────────
    private const int ColItemNo   = 1;  // A
    private const int ColRef      = 2;  // B
    private const int ColDesc     = 3;  // C
    private const int ColStock    = 4;  // D
    private const int ColCategory = 5;  // E
    private const int ColUnit     = 6;  // F
    private const int ColUnitPrice = 7; // G
    private const int ColQty      = 8;  // H
    private const int ColEstBudget = 9; // I
    private const int ColQ1Qty    = 10; // J
    private const int ColQ1Amt    = 11; // K
    private const int ColQ2Qty    = 12; // L
    private const int ColQ2Amt    = 13; // M
    private const int ColQ3Qty    = 14; // N
    private const int ColQ3Amt    = 15; // O
    private const int ColQ4Qty    = 16; // P
    private const int ColQ4Amt    = 17; // Q
    private const int ColTotal    = 18; // R
    private const int LastCol     = 18; // R

    // ── Style catalog ──────────────────────────────────────────────────────────
    private static readonly XLColor ClrColHeader = XLColor.FromHtml("#A8D08D"); // green column header
    private static readonly XLColor ClrProgram   = XLColor.FromHtml("#D9D9D9"); // program section
    private static readonly XLColor ClrProject   = XLColor.FromHtml("#F2F2F2"); // project section
    private static readonly XLColor ClrAccount   = XLColor.FromHtml("#E2EFDA"); // account subsection
    private static readonly XLColor ClrFundTotal = XLColor.FromHtml("#FFC000"); // fund grand total
    private const string FontName      = "Arial Narrow";
    private const double FontSize      = 11;
    private const string AccountingFmt = "_(* #,##0.00_);_(* \\(#,##0.00\\);_(* \"-\"??_);_(@_)";
    private const string QtyFmt        = "#,##0.###;;\"-\""; // qty: plain, blank/zero renders "-"

    // index = column number (1-based); 0 = unused slot 0.
    private static readonly double[] ColWidths =
    {
        0, 8, 24, 40, 16, 18, 8, 13, 8, 15, 8, 15, 8, 15, 8, 15, 8, 15, 15,
    };

    public byte[] Export(PpmpReportDto report)
    {
        using XLWorkbook wb = new();

        foreach (PpmpReportFundSourceDto fund in report.FundSourceReports)
            WriteFundSheet(wb, report, fund);

        // Always emit at least one sheet so the workbook is valid even when there's no data.
        if (report.FundSourceReports.Count == 0)
            WriteFundSheet(wb, report, new PpmpReportFundSourceDto("GENERAL FUND", [], 0m));

        using MemoryStream ms = new();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private void WriteFundSheet(XLWorkbook wb, PpmpReportDto report, PpmpReportFundSourceDto fund)
    {
        IXLWorksheet ws = wb.AddWorksheet(SafeSheetName(fund.FundSourceName, wb));

        ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;

        for (int c = ColItemNo; c <= LastCol; c++)
            if (ColWidths[c] > 0) ws.Column(c).Width = ColWidths[c];

        ws.Style.Font.SetFontName(FontName);
        ws.Style.Font.SetFontSize(FontSize);

        int row = 2;

        MergeH(ws, row, ColItemNo, LastCol, "PROJECT PROCUREMENT MANAGEMENT PLAN (PPMP)",
            s => s.Font.SetBold(true).Font.SetFontSize(14)
                  .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        MergeH(ws, row, ColItemNo, LastCol, $"FY {report.FiscalYear}",
            s => s.Font.SetBold(true).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        string endUser = string.IsNullOrWhiteSpace(report.DivisionName)
            ? report.OfficeName
            : $"{report.OfficeName} — {report.DivisionName}";
        MergeH(ws, row, ColItemNo, LastCol, $"END-USER/UNIT: {endUser}",
            s => s.Font.SetBold(true).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        MergeH(ws, row, ColItemNo, LastCol, $"Charged to: {fund.FundSourceName}",
            s => s.Font.SetBold(true).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row += 2;

        int headerRow = row;
        WriteColumnHeaders(ws, row);
        row++;

        int itemNo = 0;
        foreach (PpmpReportProgramDto program in fund.Programs)
            row = WriteProgram(ws, program, row, ref itemNo);

        WriteFundTotalRow(ws, row, $"TOTAL — {fund.FundSourceName}", fund.Total);
        int lastDataRow = row;
        row++;

        // Grid border around the header + data block.
        ws.Range(headerRow, ColItemNo, lastDataRow, LastCol).Style
            .Border.SetInsideBorder(XLBorderStyleValues.Thin)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin);

        WriteSignatoryBlock(ws, row + 2);
    }

    private void WriteColumnHeaders(IXLWorksheet ws, int row)
    {
        (int col, string label)[] headers =
        {
            (ColItemNo, "Item No."),
            (ColRef, "AIP Reference Code"),
            (ColDesc, "Description"),
            (ColStock, "Stock Card No."),
            (ColCategory, "Short Category"),
            (ColUnit, "Unit"),
            (ColUnitPrice, "Unit Price"),
            (ColQty, "Qty"),
            (ColEstBudget, "Est. Budget"),
            (ColQ1Qty, "1st Qty"),
            (ColQ1Amt, "1st Amount"),
            (ColQ2Qty, "2nd Qty"),
            (ColQ2Amt, "2nd Amount"),
            (ColQ3Qty, "3rd Qty"),
            (ColQ3Amt, "3rd Amount"),
            (ColQ4Qty, "4th Qty"),
            (ColQ4Amt, "4th Amount"),
            (ColTotal, "Total"),
        };
        foreach ((int col, string label) in headers)
            ws.Cell(row, col).Value = label;

        ws.Range(row, ColItemNo, row, LastCol).Style
            .Fill.SetBackgroundColor(ClrColHeader)
            .Font.SetBold(true)
            .Alignment.SetWrapText(true)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
            .Border.SetInsideBorder(XLBorderStyleValues.Thin);
        ws.Row(row).Height = 30;
    }

    private int WriteProgram(IXLWorksheet ws, PpmpReportProgramDto program, int row, ref int itemNo)
    {
        WriteSectionRow(ws, row, program.RefCode, program.Name, ClrProgram);
        row++;
        foreach (PpmpReportProjectDto project in program.Projects)
            row = WriteProject(ws, project, row, ref itemNo);
        return row;
    }

    private int WriteProject(IXLWorksheet ws, PpmpReportProjectDto project, int row, ref int itemNo)
    {
        WriteSectionRow(ws, row, project.RefCode, project.Name, ClrProject);
        row++;
        foreach (PpmpReportActivityDto activity in project.Activities)
            row = WriteActivity(ws, activity, row, ref itemNo);
        return row;
    }

    private int WriteActivity(IXLWorksheet ws, PpmpReportActivityDto activity, int row, ref int itemNo)
    {
        WriteSectionRow(ws, row, activity.RefCode, activity.Name, fill: null);
        row++;
        foreach (PpmpReportAccountDto account in activity.Accounts)
            row = WriteAccount(ws, account, row, ref itemNo);
        return row;
    }

    private int WriteAccount(IXLWorksheet ws, PpmpReportAccountDto account, int row, ref int itemNo)
    {
        // Account subsection header: label merged across the descriptive columns, its total
        // (= sum of the items below — Q11) in the Est. Budget column.
        string label = string.IsNullOrWhiteSpace(account.AccountNumber)
            ? (account.AccountTitle ?? "—")
            : $"{account.AccountNumber} — {account.AccountTitle}";
        ws.Range(row, ColRef, row, ColQty).Merge();
        ws.Cell(row, ColRef).Value = label;
        ws.Cell(row, ColEstBudget).Value = account.Total;
        ws.Cell(row, ColEstBudget).Style.NumberFormat.SetFormat(AccountingFmt);
        ws.Range(row, ColItemNo, row, LastCol).Style
            .Fill.SetBackgroundColor(ClrAccount).Font.SetBold(true);
        row++;

        foreach (PpmpReportItemDto item in account.Items)
        {
            itemNo++;
            WriteItemRow(ws, row, itemNo, item);
            row++;
        }
        return row;
    }

    private static void WriteItemRow(IXLWorksheet ws, int row, int itemNo, PpmpReportItemDto item)
    {
        ws.Cell(row, ColItemNo).Value = itemNo;
        ws.Cell(row, ColItemNo).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Cell(row, ColDesc).Value = item.Description;
        ws.Cell(row, ColStock).Value = item.StockCardNo ?? "";
        ws.Cell(row, ColCategory).Value = item.Category ?? "";
        ws.Cell(row, ColUnit).Value = item.Unit;

        ws.Cell(row, ColUnitPrice).Value = item.UnitPrice;
        ws.Cell(row, ColEstBudget).Value = item.EstBudget;
        ws.Cell(row, ColTotal).Value = item.EstBudget;
        ws.Cell(row, ColQ1Amt).Value = item.Q1Amount;
        ws.Cell(row, ColQ2Amt).Value = item.Q2Amount;
        ws.Cell(row, ColQ3Amt).Value = item.Q3Amount;
        ws.Cell(row, ColQ4Amt).Value = item.Q4Amount;
        foreach (int c in new[] { ColUnitPrice, ColEstBudget, ColTotal, ColQ1Amt, ColQ2Amt, ColQ3Amt, ColQ4Amt })
            ws.Cell(row, c).Style.NumberFormat.SetFormat(AccountingFmt);

        ws.Cell(row, ColQty).Value = item.Qty;
        ws.Cell(row, ColQ1Qty).Value = item.Q1Qty;
        ws.Cell(row, ColQ2Qty).Value = item.Q2Qty;
        ws.Cell(row, ColQ3Qty).Value = item.Q3Qty;
        ws.Cell(row, ColQ4Qty).Value = item.Q4Qty;
        foreach (int c in new[] { ColQty, ColQ1Qty, ColQ2Qty, ColQ3Qty, ColQ4Qty })
            ws.Cell(row, c).Style.NumberFormat.SetFormat(QtyFmt);
    }

    /// <summary>Program/project/activity section row: AIP ref in column B, name merged across C–R. Item No. (A) stays blank — it belongs only to item rows.</summary>
    private static void WriteSectionRow(IXLWorksheet ws, int row, string refCode, string name, XLColor? fill)
    {
        ws.Cell(row, ColRef).Value = refCode;
        MergeH(ws, row, ColDesc, LastCol, name, s => { });
        ws.Range(row, ColItemNo, row, LastCol).Style.Font.SetBold(true);
        if (fill is not null)
            ws.Range(row, ColItemNo, row, LastCol).Style.Fill.SetBackgroundColor(fill);
    }

    private static void WriteFundTotalRow(IXLWorksheet ws, int row, string label, decimal total)
    {
        ws.Range(row, ColItemNo, row, ColQty).Merge();
        ws.Cell(row, ColItemNo).Value = label;
        ws.Cell(row, ColEstBudget).Value = total;
        ws.Cell(row, ColTotal).Value = total;
        ws.Cell(row, ColEstBudget).Style.NumberFormat.SetFormat(AccountingFmt);
        ws.Cell(row, ColTotal).Style.NumberFormat.SetFormat(AccountingFmt);
        ws.Range(row, ColItemNo, row, LastCol).Style
            .Font.SetBold(true)
            .Fill.SetBackgroundColor(ClrFundTotal)
            .Border.SetTopBorder(XLBorderStyleValues.Thin)
            .Border.SetBottomBorder(XLBorderStyleValues.Thin);
    }

    private static void WriteSignatoryBlock(IXLWorksheet ws, int row)
    {
        ws.Cell(row, ColRef).Value = "Prepared by:";
        ws.Cell(row, ColRef).Style.Font.SetBold(true);
        ws.Cell(row, ColQ2Qty).Value = "Noted by:";
        ws.Cell(row, ColQ2Qty).Style.Font.SetBold(true);

        int lineRow = row + 3;
        ws.Range(lineRow, ColRef, lineRow, ColUnit).Merge();
        ws.Cell(lineRow, ColRef).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
        ws.Cell(lineRow + 1, ColRef).Value = "Signature over Printed Name / Position";
        ws.Cell(lineRow + 1, ColRef).Style.Font.SetFontSize(9);

        ws.Range(lineRow, ColQ2Qty, lineRow, ColQ4Qty).Merge();
        ws.Cell(lineRow, ColQ2Qty).Style.Border.SetTopBorder(XLBorderStyleValues.Thin);
        ws.Cell(lineRow + 1, ColQ2Qty).Value = "Signature over Printed Name / Position";
        ws.Cell(lineRow + 1, ColQ2Qty).Style.Font.SetFontSize(9);
    }

    private static void MergeH(IXLWorksheet ws, int row, int colFrom, int colTo, string value, Action<IXLStyle> style)
    {
        ws.Range(row, colFrom, row, colTo).Merge();
        ws.Cell(row, colFrom).Value = value;
        style(ws.Cell(row, colFrom).Style);
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
