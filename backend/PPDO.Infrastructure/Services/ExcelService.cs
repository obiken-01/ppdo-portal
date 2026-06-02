using ClosedXML.Excel;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Excel (.xlsx) import/export using ClosedXML.
///
/// Three responsibilities:
///   1. <see cref="GeneratePRTemplate"/>  — blank PR template for user download
///   2. <see cref="ExportPRReport"/>      — filled PR Report (Sections 1–3)
///   3. <see cref="ParsePRImport"/>       — parse uploaded template into domain data
///
/// Layout constants are <c>internal</c> so <c>ExcelServiceTests</c> can reference them
/// directly to build test files that match the exact same cell addresses.
///
/// Colour convention (PPDO design system):
///   Yellow (#FFFDE7) = user fills in
///   Gray   (#F1F3F5) = auto-fill / locked — do not edit
///   Green  (#F0FAF4) = section headers
/// </summary>
public sealed class ExcelService : IExcelService
{
    // ── Layout constants ──────────────────────────────────────────────────────
    // Section 1 — col A = label, col B = value.

    internal const int ROW_DEPARTMENT         = 3;
    internal const int ROW_DIVISION           = 4;
    internal const int ROW_FUND               = 5;
    internal const int ROW_REQUESTED_BY       = 6;
    internal const int ROW_POSITION           = 7;
    internal const int ROW_PR_DATE            = 8;
    internal const int ROW_APPROVED_BY        = 9;
    internal const int ROW_APPROVING_POSITION = 10;
    internal const int ROW_AIP_CODE           = 11;
    internal const int ROW_ACCOUNT_NO         = 12;
    internal const int ROW_ACCOUNT_TITLE      = 13;
    internal const int ROW_PROGRAM            = 14;
    internal const int ROW_PROJECT            = 15;
    internal const int ROW_ACTIVITY           = 16;
    internal const int ROW_SAI_NO             = 17;
    internal const int ROW_ALOBS_NO           = 18;

    // Section 2 — items grid.
    internal const int ROW_SECTION2_HEADER = 20;
    internal const int ROW_ITEMS_HEADER    = 21;
    internal const int ROW_ITEMS_START     = 22;
    internal const int ROW_ITEMS_END       = 51;   // 30 rows

    // Section 2 columns (1-based).
    internal const int COL_ITEM_NO     = 1; // A
    internal const int COL_STOCK_NO    = 2; // B
    internal const int COL_DESCRIPTION = 3; // C
    internal const int COL_UNIT        = 4; // D
    internal const int COL_QTY         = 5; // E
    internal const int COL_UNIT_COST   = 6; // F
    internal const int COL_TOTAL_COST  = 7; // G

    // Section 3 — distribution (export only, starts after Section 2).
    private const int ROW_SECTION3_OFFSET = 5; // rows below last item row

    // ── Colours ───────────────────────────────────────────────────────────────

    private static readonly XLColor Yellow = XLColor.FromHtml("#FFFDE7");
    private static readonly XLColor Gray   = XLColor.FromHtml("#F1F3F5");
    private static readonly XLColor Green  = XLColor.FromHtml("#F0FAF4");
    private static readonly XLColor White  = XLColor.White;
    private static readonly XLColor DarkGreen = XLColor.FromHtml("#1F7A45");

    // ── GeneratePRTemplate ────────────────────────────────────────────────────

    /// <inheritdoc />
    public byte[] GeneratePRTemplate()
    {
        using XLWorkbook wb = new();

        IXLWorksheet ws = wb.AddWorksheet("PR-001");
        BuildPRSheet(ws, prefilled: false, pr: null);

        IXLWorksheet inst = wb.AddWorksheet("Instructions");
        BuildInstructionsSheet(inst);

        using MemoryStream ms = new();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── ExportPRReport ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public byte[] ExportPRReport(PurchaseRequest pr)
    {
        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet("PR Report");
        BuildPRSheet(ws, prefilled: true, pr: pr);
        BuildDistributionSection(ws, pr);

        using MemoryStream ms = new();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── ParsePRImport ─────────────────────────────────────────────────────────

    /// <inheritdoc />
    public IReadOnlyList<PurchaseRequestImportRow> ParsePRImport(Stream stream)
    {
        using XLWorkbook wb = new(stream);

        List<string> errors  = new();
        List<PurchaseRequestImportRow> results = new();

        foreach (IXLWorksheet ws in wb.Worksheets)
        {
            // Skip the Instructions sheet.
            if (ws.Name.Equals("Instructions", StringComparison.OrdinalIgnoreCase))
                continue;

            List<string> sheetErrors = new();

            // ── Section 1 validation ──────────────────────────────────────────

            string divisionRaw   = ws.Cell(ROW_DIVISION,     2).GetString().Trim();
            string requestedBy   = ws.Cell(ROW_REQUESTED_BY, 2).GetString().Trim();
            string prDateRaw     = ws.Cell(ROW_PR_DATE,      2).GetString().Trim();

            if (string.IsNullOrWhiteSpace(divisionRaw))
                sheetErrors.Add($"[{ws.Name}] Division is required.");

            if (string.IsNullOrWhiteSpace(requestedBy))
                sheetErrors.Add($"[{ws.Name}] Requested By is required.");

            Division division = Division.Admin;
            if (!string.IsNullOrWhiteSpace(divisionRaw)
                && !Enum.TryParse(divisionRaw, ignoreCase: true, out division))
            {
                sheetErrors.Add($"[{ws.Name}] Division '{divisionRaw}' is not valid. " +
                                $"Valid values: Admin, Planning, RM, MIS, SPD.");
            }

            DateOnly prDate = DateOnly.MinValue;
            if (string.IsNullOrWhiteSpace(prDateRaw))
            {
                sheetErrors.Add($"[{ws.Name}] PR Date is required.");
            }
            else
            {
                IXLCell dateCell = ws.Cell(ROW_PR_DATE, 2);
                if (dateCell.DataType == XLDataType.DateTime)
                {
                    prDate = DateOnly.FromDateTime(dateCell.GetDateTime());
                }
                else if (DateTime.TryParse(prDateRaw, out DateTime parsed))
                {
                    prDate = DateOnly.FromDateTime(parsed);
                }
                else
                {
                    sheetErrors.Add($"[{ws.Name}] PR Date '{prDateRaw}' is not a valid date.");
                }
            }

            // ── Section 2 — item rows ─────────────────────────────────────────

            List<PRItemImportRow> items = new();

            for (int row = ROW_ITEMS_START; row <= ROW_ITEMS_END; row++)
            {
                string stockNo   = ws.Cell(row, COL_STOCK_NO)   .GetString().Trim();
                string desc      = ws.Cell(row, COL_DESCRIPTION).GetString().Trim();
                string unit      = ws.Cell(row, COL_UNIT)       .GetString().Trim();
                string qtyRaw    = ws.Cell(row, COL_QTY)        .GetString().Trim();
                string costRaw   = ws.Cell(row, COL_UNIT_COST)  .GetString().Trim();

                // Skip rows where both Description and StockNo are blank.
                if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(stockNo))
                    continue;

                if (!decimal.TryParse(qtyRaw, out decimal qty) || qty <= 0)
                {
                    sheetErrors.Add(
                        $"[{ws.Name}] Row {row}: Qty must be a positive number (got '{qtyRaw}').");
                    continue;
                }

                decimal unitCost = 0m;
                if (!string.IsNullOrWhiteSpace(costRaw))
                    decimal.TryParse(costRaw, out unitCost);

                items.Add(new PRItemImportRow
                {
                    StockNo         = string.IsNullOrWhiteSpace(stockNo) ? null : stockNo,
                    Description     = string.IsNullOrWhiteSpace(desc) ? stockNo : desc,
                    Unit            = string.IsNullOrWhiteSpace(unit) ? "pcs" : unit,
                    Quantity        = qty,
                    UnitCost        = unitCost,
                    IsUnknownStock  = !string.IsNullOrWhiteSpace(stockNo), // caller resolves
                });
            }

            if (items.Count == 0)
                sheetErrors.Add($"[{ws.Name}] At least one item row with Qty > 0 is required.");

            errors.AddRange(sheetErrors);

            if (sheetErrors.Count == 0)
            {
                results.Add(new PurchaseRequestImportRow
                {
                    SheetName           = ws.Name,
                    Division            = division,
                    RequestedBy         = requestedBy,
                    PRDate              = prDate,
                    Department          = ws.Cell(ROW_DEPARTMENT,         2).GetString().Trim() is { Length: > 0 } d ? d : "PPDO",
                    Fund                = NullIfBlank(ws.Cell(ROW_FUND,               2).GetString()),
                    Position            = NullIfBlank(ws.Cell(ROW_POSITION,           2).GetString()),
                    ApprovedBy          = NullIfBlank(ws.Cell(ROW_APPROVED_BY,        2).GetString()),
                    ApprovingPosition   = NullIfBlank(ws.Cell(ROW_APPROVING_POSITION, 2).GetString()),
                    AIPCode             = NullIfBlank(ws.Cell(ROW_AIP_CODE,           2).GetString()),
                    AccountNo           = NullIfBlank(ws.Cell(ROW_ACCOUNT_NO,         2).GetString()),
                    AccountTitle        = NullIfBlank(ws.Cell(ROW_ACCOUNT_TITLE,      2).GetString()),
                    Program             = NullIfBlank(ws.Cell(ROW_PROGRAM,            2).GetString()),
                    Project             = NullIfBlank(ws.Cell(ROW_PROJECT,            2).GetString()),
                    Activity            = NullIfBlank(ws.Cell(ROW_ACTIVITY,           2).GetString()),
                    SAINo               = NullIfBlank(ws.Cell(ROW_SAI_NO,             2).GetString()),
                    ALOBSNo             = NullIfBlank(ws.Cell(ROW_ALOBS_NO,           2).GetString()),
                    Items               = items,
                });
            }
        }

        if (errors.Count > 0)
            throw new ExcelParseException(errors);

        return results;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Builds the PR sheet either as a blank template (yellow cells for user input)
    /// or as a pre-filled report (all cells show data, gray background).
    /// </summary>
    private static void BuildPRSheet(IXLWorksheet ws, bool prefilled, PurchaseRequest? pr)
    {
        // ── Title ─────────────────────────────────────────────────────────────
        ws.Cell(1, 1).Value = "PURCHASE REQUEST";
        ws.Cell(1, 1).Style
            .Font.SetBold(true)
            .Font.SetFontSize(14)
            .Font.SetFontColor(DarkGreen)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Range(1, 1, 1, 7).Merge();

        ws.Cell(2, 1).Value = "";

        // ── Section 1 labels ──────────────────────────────────────────────────
        void Label(int row, string text) =>
            ws.Cell(row, 1).Value = text;

        void Value(int row, object? val, bool isAuto = false)
        {
            IXLCell cell = ws.Cell(row, 2);
            if (val != null) cell.Value = XLCellValue.FromObject(val);
            cell.Style.Fill.SetBackgroundColor(isAuto || prefilled ? Gray : Yellow);
            if (!prefilled && !isAuto)
                cell.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                    .Border.SetOutsideBorderColor(XLColor.FromHtml("#ADB5BD"));
        }

        Label(ROW_DEPARTMENT,         "Department");
        Label(ROW_DIVISION,           "Division *");
        Label(ROW_FUND,               "Fund");
        Label(ROW_REQUESTED_BY,       "Requested By *");
        Label(ROW_POSITION,           "Position");
        Label(ROW_PR_DATE,            "PR Date *");
        Label(ROW_APPROVED_BY,        "Approved By");
        Label(ROW_APPROVING_POSITION, "Approving Position");
        Label(ROW_AIP_CODE,           "AIP Code");
        Label(ROW_ACCOUNT_NO,         "Account No.");
        Label(ROW_ACCOUNT_TITLE,      "Account Title");
        Label(ROW_PROGRAM,            "Program");
        Label(ROW_PROJECT,            "Project");
        Label(ROW_ACTIVITY,           "Activity");
        Label(ROW_SAI_NO,             "SAI No.");
        Label(ROW_ALOBS_NO,           "ALOBS No.");

        // Style labels
        for (int r = ROW_DEPARTMENT; r <= ROW_ALOBS_NO; r++)
            ws.Cell(r, 1).Style.Font.SetBold(true).Fill.SetBackgroundColor(Gray);

        // ── Section 1 values ──────────────────────────────────────────────────
        Value(ROW_DEPARTMENT,         prefilled ? pr!.Department  : "PPDO",   isAuto: true);
        Value(ROW_DIVISION,           prefilled ? pr!.Division.ToString()      : null);
        Value(ROW_FUND,               prefilled ? pr!.Fund                     : null);
        Value(ROW_REQUESTED_BY,       prefilled ? pr!.RequestedBy              : null);
        Value(ROW_POSITION,           prefilled ? pr!.Position                 : null);
        Value(ROW_PR_DATE,            prefilled ? pr!.PRDate.ToDateTime(TimeOnly.MinValue) : null);
        Value(ROW_APPROVED_BY,        prefilled ? pr!.ApprovedBy               : null);
        Value(ROW_APPROVING_POSITION, prefilled ? pr!.ApprovingPosition        : null);
        Value(ROW_AIP_CODE,           prefilled ? pr!.AIPCode                  : null);
        Value(ROW_ACCOUNT_NO,         prefilled ? pr!.AccountNo                : null);
        Value(ROW_ACCOUNT_TITLE,      prefilled ? pr!.AccountTitle             : null);
        Value(ROW_PROGRAM,            prefilled ? pr!.Program                  : null);
        Value(ROW_PROJECT,            prefilled ? pr!.Project                  : null);
        Value(ROW_ACTIVITY,           prefilled ? pr!.Activity                 : null);
        Value(ROW_SAI_NO,             prefilled ? pr!.SAINo                    : null);
        Value(ROW_ALOBS_NO,           prefilled ? pr!.ALOBSNo                  : null);

        // PR No. shown in report mode
        if (prefilled)
        {
            ws.Cell(ROW_DEPARTMENT - 1, 4).Value = "PR No.";
            ws.Cell(ROW_DEPARTMENT - 1, 5).Value = pr!.PRNo;
            ws.Cell(ROW_DEPARTMENT - 1, 4).Style.Font.SetBold(true).Fill.SetBackgroundColor(Gray);
            ws.Cell(ROW_DEPARTMENT - 1, 5).Style.Fill.SetBackgroundColor(Gray);
        }

        // ── Section 2 header ──────────────────────────────────────────────────
        ws.Cell(ROW_SECTION2_HEADER, 1).Value = "SECTION 2 — ITEMS";
        ws.Cell(ROW_SECTION2_HEADER, 1).Style
            .Font.SetBold(true)
            .Fill.SetBackgroundColor(Green)
            .Font.SetFontColor(DarkGreen);
        ws.Range(ROW_SECTION2_HEADER, 1, ROW_SECTION2_HEADER, 7).Merge();

        // Column headers
        string[] headers = ["Item No.", "Stock No.", "Description", "Unit", "Qty", "Unit Cost", "Total Cost"];
        for (int c = 1; c <= 7; c++)
        {
            IXLCell h = ws.Cell(ROW_ITEMS_HEADER, c);
            h.Value = headers[c - 1];
            h.Style.Font.SetBold(true).Fill.SetBackgroundColor(DarkGreen)
                .Font.SetFontColor(White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        // ── Section 2 rows ────────────────────────────────────────────────────
        if (prefilled && pr!.Items.Any())
        {
            // Filled export — write actual PR item data
            int rowIdx = ROW_ITEMS_START;
            foreach (PRItem item in pr.Items.OrderBy(i => i.ItemNo))
            {
                ws.Cell(rowIdx, COL_ITEM_NO)    .Value = item.ItemNo;
                ws.Cell(rowIdx, COL_STOCK_NO)   .Value = item.StockNo ?? "";
                ws.Cell(rowIdx, COL_DESCRIPTION).Value = item.Description;
                ws.Cell(rowIdx, COL_UNIT)       .Value = item.Unit;
                ws.Cell(rowIdx, COL_QTY)        .Value = item.Quantity;
                ws.Cell(rowIdx, COL_UNIT_COST)  .Value = item.UnitCost;
                ws.Cell(rowIdx, COL_TOTAL_COST) .Value = item.TotalCost;

                for (int c = 1; c <= 7; c++)
                    ws.Cell(rowIdx, c).Style.Fill.SetBackgroundColor(rowIdx % 2 == 0 ? White : Gray);

                rowIdx++;
            }

            // Total row
            ws.Cell(rowIdx, COL_DESCRIPTION).Value = "TOTAL";
            ws.Cell(rowIdx, COL_DESCRIPTION).Style.Font.SetBold(true);
            ws.Cell(rowIdx, COL_TOTAL_COST) .Value = pr.TotalAmount;
            ws.Cell(rowIdx, COL_TOTAL_COST) .Style.Font.SetBold(true)
                .Fill.SetBackgroundColor(Green);
        }
        else
        {
            // Blank template — yellow input cells
            for (int row = ROW_ITEMS_START; row <= ROW_ITEMS_END; row++)
            {
                ws.Cell(row, COL_ITEM_NO)    .Value = row - ROW_ITEMS_START + 1;
                ws.Cell(row, COL_ITEM_NO)    .Style.Fill.SetBackgroundColor(Gray);
                ws.Cell(row, COL_STOCK_NO)   .Style.Fill.SetBackgroundColor(Yellow);
                ws.Cell(row, COL_DESCRIPTION).Style.Fill.SetBackgroundColor(Yellow);
                ws.Cell(row, COL_UNIT)       .Style.Fill.SetBackgroundColor(Yellow);
                ws.Cell(row, COL_QTY)        .Style.Fill.SetBackgroundColor(Yellow);
                ws.Cell(row, COL_UNIT_COST)  .Style.Fill.SetBackgroundColor(Yellow);
                ws.Cell(row, COL_TOTAL_COST) .Style.Fill.SetBackgroundColor(Gray);
            }
        }

        // ── Column widths ─────────────────────────────────────────────────────
        ws.Column(COL_ITEM_NO)    .Width = 8;
        ws.Column(COL_STOCK_NO)   .Width = 14;
        ws.Column(COL_DESCRIPTION).Width = 40;
        ws.Column(COL_UNIT)       .Width = 8;
        ws.Column(COL_QTY)        .Width = 8;
        ws.Column(COL_UNIT_COST)  .Width = 12;
        ws.Column(COL_TOTAL_COST) .Width = 14;

        // Section 1 label/value columns
        ws.Column(1).Width = 22;
        ws.Column(2).Width = 28;
    }

    /// <summary>
    /// Appends Section 3 (distribution summary) below the items grid.
    /// Only called for <see cref="ExportPRReport"/>.
    /// </summary>
    private static void BuildDistributionSection(IXLWorksheet ws, PurchaseRequest pr)
    {
        // Find first empty row after Section 2
        int lastItemRow = pr.Items.Any()
            ? ROW_ITEMS_START + pr.Items.Count
            : ROW_ITEMS_END;

        int sec3Start = lastItemRow + ROW_SECTION3_OFFSET;

        ws.Cell(sec3Start, 1).Value = "SECTION 3 — DISTRIBUTION";
        ws.Cell(sec3Start, 1).Style
            .Font.SetBold(true)
            .Fill.SetBackgroundColor(Green)
            .Font.SetFontColor(DarkGreen);
        ws.Range(sec3Start, 1, sec3Start, 7).Merge();

        int headerRow = sec3Start + 1;
        string[] dist3Headers = ["Item No.", "Description", "Unit", "Qty Delivered", "Division", "Qty Issued", "Issue Ref"];
        for (int c = 1; c <= 7; c++)
        {
            IXLCell h = ws.Cell(headerRow, c);
            h.Value = dist3Headers[c - 1];
            h.Style.Font.SetBold(true).Fill.SetBackgroundColor(DarkGreen)
                .Font.SetFontColor(White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        int dataRow = headerRow + 1;
        foreach (Delivery delivery in pr.Deliveries)
        {
            foreach (DeliveryItem di in delivery.Items)
            {
                foreach (Distribution dist in di.Distributions)
                {
                    ws.Cell(dataRow, 1).Value = di.PRItem?.ItemNo ?? 0;
                    ws.Cell(dataRow, 2).Value = di.PRItem?.Description ?? "";
                    ws.Cell(dataRow, 3).Value = di.PRItem?.Unit ?? "";
                    ws.Cell(dataRow, 4).Value = di.QtyDelivered;
                    ws.Cell(dataRow, 5).Value = dist.Division.ToString();
                    ws.Cell(dataRow, 6).Value = dist.QtyIssued;
                    ws.Cell(dataRow, 7).Value = dist.IssueRef;

                    for (int c = 1; c <= 7; c++)
                        ws.Cell(dataRow, c).Style.Fill.SetBackgroundColor(dataRow % 2 == 0 ? White : Gray);

                    dataRow++;
                }
            }
        }

        if (dataRow == headerRow + 1)
        {
            ws.Cell(dataRow, 1).Value = "No distributions recorded.";
            ws.Range(dataRow, 1, dataRow, 7).Merge();
            ws.Cell(dataRow, 1).Style.Fill.SetBackgroundColor(Gray)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }
    }

    /// <summary>
    /// Populates the Instructions worksheet explaining the template fill rules.
    /// </summary>
    private static void BuildInstructionsSheet(IXLWorksheet ws)
    {
        ws.Cell(1, 1).Value = "HOW TO USE THIS TEMPLATE";
        ws.Cell(1, 1).Style.Font.SetBold(true).Font.SetFontSize(13).Font.SetFontColor(DarkGreen);

        string[] lines =
        [
            "",
            "COLOUR GUIDE:",
            "  Yellow cells (#FFFDE7) = Fill in these cells.",
            "  Gray cells   (#F1F3F5) = Do not edit — auto-filled or locked.",
            "",
            "SECTION 1 — PR HEADER:",
            "  Fields marked with * are required: Division, Requested By, PR Date.",
            "  Division must be one of: Admin, Planning, RM, MIS, SPD.",
            "  PR Date format: DD/MM/YYYY  (e.g. 01/06/2026)",
            "",
            "SECTION 2 — ITEMS:",
            "  Enter at least one item row with Qty > 0.",
            "  If you know the Stock No., enter it — Description and Unit will be auto-filled",
            "    when the file is imported into the portal.",
            "  Leave Stock No. blank if the item is not yet in the catalog.",
            "  Rows where both Stock No. and Description are blank will be skipped.",
            "",
            "MULTIPLE PRs IN ONE UPLOAD:",
            "  Duplicate the sheet tab (right-click → Move or Copy → tick 'Create a copy').",
            "  Rename the duplicate tab to PR-002, PR-003, etc.",
            "  Each sheet tab represents one Purchase Request.",
            "  The Instructions sheet is ignored during import.",
            "",
            "DEFAULT PASSWORD:",
            "  If you forget your portal password, contact your System Administrator.",
            "  Default password (after reset): PPDOUser2026!",
        ];

        for (int i = 0; i < lines.Length; i++)
        {
            ws.Cell(i + 2, 1).Value = lines[i];
            if (lines[i].EndsWith(':'))
                ws.Cell(i + 2, 1).Style.Font.SetBold(true);
        }

        ws.Column(1).Width = 80;
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
