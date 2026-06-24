using ClosedXML.Excel;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
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
public sealed class ExcelService : IExcelService, IWfpExcelService
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
        BuildReportSheet(ws, pr);           // new — matches the UI layout

        using MemoryStream ms = new();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── BuildReportSheet — export-only, matches UI layout ────────────────────
    //
    // Column layout (A–H, 8 columns):
    //   Section 1: A-D = left label/value pair, E-H = right label/value pair
    //   Section 2: # | Description | Stock No. | Unit |
    //              Qty Ordered | Qty Delivered | Qty Distributed | Remaining
    //   Section 3: Item# | Description | Unit | Qty Delivered | Delivery Ref |
    //              Delivery Date | Division | Qty Issued | Issue Ref |
    //              Date Issued | Issued By | Remarks  (12 columns A–L)

    private static void BuildReportSheet(IXLWorksheet ws, PurchaseRequest pr)
    {
        // ── Shared colours ────────────────────────────────────────────────────

        XLColor MedGreen  = XLColor.FromHtml("#2E9958");
        XLColor LightGray = XLColor.FromHtml("#F1F3F5");
        XLColor LightGreen = XLColor.FromHtml("#F0FAF4");
        XLColor Blue      = XLColor.FromHtml("#378ADD");
        XLColor Amber     = XLColor.FromHtml("#EF9F27");
        XLColor Red       = XLColor.FromHtml("#E24B4A");
        XLColor LabelBg   = XLColor.FromHtml("#E9ECEF");

        // ── Section 1 — Header ────────────────────────────────────────────────
        //
        // Full layout uses 10 columns (A–J):
        //   Title / Division / Section-1 banner  → A:J merged
        //   Pair rows:
        //     Col B (2)   = left label  (bold, right-aligned, sz 11, no fill)
        //     Col C (3)   = left value  (single cell, sz 11, no fill)
        //     Col D–F     = empty spacers
        //     Col G (7)   = right label (bold, right-aligned, sz 11, no fill)
        //     Col H (8)   = right value (single cell, sz 11, no fill)
        //   FullRow (AccountTitle, Program, Project, Activity):
        //     Col B (2)   = label, Col C:G (3–7) merged = value
        //   PR No. row:
        //     B=label "PR No.:", C=value, G:I (7–9) merged = "Status:  X"
        //   Total Amount:
        //     A:H (1–8) merged = label, I:J (9–10) merged = ₱ value
        //   Delivery bar (row after blank):
        //     A:B (1–2) = Delivery count (amber)
        //     C:E (3–5) = Status (light green)
        //     F:H (6–8) = Fulfillment % (teal)
        //     I:J (9–10)= Total (navy)

        // Row 1: Main title — A:J
        ws.Cell(1, 1).Value = "📋  PURCHASE REQUEST REPORT";
        ws.Cell(1, 1).Style
            .Font.SetBold(true)
            .Font.SetFontSize(14)
            .Font.SetFontColor(White)
            .Fill.SetBackgroundColor(DarkGreen)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Range(1, 1, 1, 10).Merge();
        ws.Row(1).Height = 28;

        // Row 2: Division sub-header — A:J
        ws.Cell(2, 1).Value = (pr.Division?.Name ?? "");
        ws.Cell(2, 1).Style
            .Font.SetFontSize(11)
            .Font.SetItalic(true)
            .Font.SetFontColor(White)
            .Fill.SetBackgroundColor(MedGreen)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        ws.Range(2, 1, 2, 10).Merge();
        ws.Row(2).Height = 18;

        // Row 3: Section 1 banner — A:J
        ws.Cell(3, 1).Value = "SECTION 1 — PURCHASE REQUEST DETAILS";
        ws.Cell(3, 1).Style.Font.SetBold(true).Font.SetFontSize(10)
            .Font.SetFontColor(White)
            .Fill.SetBackgroundColor(DarkGreen)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        ws.Range(3, 1, 3, 10).Merge();
        ws.Row(3).Height = 18;

        int r = 4;  // current row pointer

        // Helpers — no fills, no borders, matching user's edited file
        void Label(int row, string text)
        {
            ws.Cell(row, 2).Value = text;
            ws.Cell(row, 2).Style
                .Font.SetBold(true)
                .Font.SetFontSize(11)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        }

        void Value(int row, int col, string? text, bool bold = false, bool wrap = false)
        {
            if (text != null) ws.Cell(row, col).Value = text;
            ws.Cell(row, col).Style
                .Font.SetBold(bold)
                .Font.SetFontSize(11);
            if (wrap) ws.Cell(row, col).Style.Alignment.SetWrapText(true);
        }

        void RLabel(int row, string text)
        {
            ws.Cell(row, 7).Value = text;
            ws.Cell(row, 7).Style
                .Font.SetBold(true)
                .Font.SetFontSize(11)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        }

        void RValue(int row, string? text, bool bold = false)
        {
            // Right value: H:I merged (cols 8–9), left-aligned
            ws.Range(row, 8, row, 9).Merge();
            if (text != null) ws.Cell(row, 8).Value = text;
            ws.Cell(row, 8).Style
                .Font.SetBold(bold)
                .Font.SetFontSize(11)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
        }

        void Pair(string labelL, string? valL, string labelR, string? valR, bool valLBold = false, bool valRBold = false)
        {
            Label(r, labelL);
            // Left value: C:D merged (cols 3–4), left-aligned
            ws.Range(r, 3, r, 4).Merge();
            Value(r, 3, valL, bold: valLBold);
            ws.Cell(r, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            RLabel(r, labelR);
            RValue(r, valR, bold: valRBold);
            ws.Row(r).Height = 18;
            r++;
        }

        void FullRow(string label, string? val, bool wrap = false)
        {
            Label(r, label);
            ws.Range(r, 3, r, 7).Merge();
            Value(r, 3, val, wrap: wrap);
            ws.Row(r).Height = wrap ? 32 : 18;
            r++;
        }

        // Row 4 — PR No. / Status — same structure as all other Pair rows
        Pair("PR No.:", pr.PRNo, "Status:", pr.Status.ToString(), valLBold: true, valRBold: true);

        Pair("PR Date",           pr.PRDate.ToShortDateString(),
             "Quarter",           ToQuarter(pr.PRDate));
        Pair("Department",        pr.Department,
             "Division",          (pr.Division?.Name ?? ""));
        Pair("Fund",              pr.Fund,
             "",                  "");
        Pair("Requested By",      pr.RequestedBy,
             "Position",          pr.Position);
        Pair("Approved By",       pr.ApprovedBy ?? "—",
             "Approving Position",pr.ApprovingPosition ?? "—");
        Pair("AIP Code",          pr.AIPCode   ?? "—",
             "Account No.",       pr.AccountNo ?? "—");
        FullRow("Account Title",  pr.AccountTitle ?? "—");
        FullRow("Program",        pr.Program      ?? "—", wrap: true);
        FullRow("Project",        pr.Project      ?? "—", wrap: true);
        FullRow("Activity",       pr.Activity     ?? "—", wrap: true);
        Pair("SAI No.",           pr.SAINo   ?? "—",
             "ALOBS No.",         pr.ALOBSNo ?? "—");

        // ── Delivery summary bar ──────────────────────────────────────────────
        // A:B = Delivery count (amber) | C:E = Status (light green)
        // F:H = Fulfillment % (teal)   | I:J = Total (navy)
        {
            int deliveryCount       = pr.Deliveries?.Count ?? 0;
            decimal totalOrdered    = pr.Items.Sum(i => i.Quantity);
            decimal totalDelivered  = pr.Deliveries?
                .SelectMany(d => d.Items)
                .GroupBy(di => di.Id)
                .Sum(g => g.First().QtyDelivered) ?? 0;
            int pct = totalOrdered > 0
                ? (int)Math.Round(totalDelivered / totalOrdered * 100)
                : 0;

            XLColor AmberBg = XLColor.FromHtml("#FEF3CD");
            XLColor TealBg  = XLColor.FromHtml("#D1ECE5");
            XLColor NavyBg  = XLColor.FromHtml("#1A3557");

            // Cell 1 — Delivery count (amber) A:B
            ws.Range(r, 1, r, 2).Merge();
            ws.Cell(r, 1).Value = $"Delivery: {deliveryCount}";
            ws.Cell(r, 1).Style.Font.SetBold(true).Font.SetFontSize(10)
                .Fill.SetBackgroundColor(AmberBg)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Cell 2 — Status (light green) C:E
            ws.Range(r, 3, r, 5).Merge();
            ws.Cell(r, 3).Value = $"Status: {pr.Status}";
            ws.Cell(r, 3).Style.Font.SetBold(true).Font.SetFontSize(10)
                .Font.SetFontColor(DarkGreen)
                .Fill.SetBackgroundColor(LightGreen)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Cell 3 — Fulfillment % (teal) F:H
            ws.Range(r, 6, r, 8).Merge();
            ws.Cell(r, 6).Value = $"{pct}% fulfilled ({(int)totalDelivered} / {(int)totalOrdered} units)";
            ws.Cell(r, 6).Style.Font.SetBold(true).Font.SetFontSize(10)
                .Fill.SetBackgroundColor(TealBg)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Cell 4 — Total (dark navy) I:J
            ws.Range(r, 9, r, 10).Merge();
            ws.Cell(r, 9).Value = $"Total: ₱{pr.TotalAmount:#,##0.00}";
            ws.Cell(r, 9).Style.Font.SetBold(true).Font.SetFontSize(10)
                .Font.SetFontColor(White)
                .Fill.SetBackgroundColor(NavyBg)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            ws.Row(r).Height = 22;
            r++;
        }

        // ── Section 2 — Line Items (UI-matching columns) ──────────────────────

        int sec2HeaderRow = r;
        ws.Cell(r, 1).Value = "SECTION 2 — LINE ITEMS: Ordered vs Delivered vs Distributed vs Remaining";
        ws.Cell(r, 1).Style.Font.SetBold(true).Font.SetFontSize(10)
            .Font.SetFontColor(White)
            .Fill.SetBackgroundColor(DarkGreen);
        ws.Range(r, 1, r, 8).Merge();
        ws.Row(r).Height = 18;
        r++;

        // Column headers
        string[] sec2Headers = ["#", "Item Description", "Stock No.", "Unit",
                                 "Qty Ordered", "Qty Delivered", "Qty Distributed", "Remaining"];
        XLColor[] sec2HeaderColors = [DarkGreen, DarkGreen, DarkGreen, DarkGreen,
                                      DarkGreen, Blue, Amber, DarkGreen];
        for (int c = 1; c <= 8; c++)
        {
            IXLCell h = ws.Cell(r, c);
            h.Value = sec2Headers[c - 1];
            h.Style.Font.SetBold(true)
                .Fill.SetBackgroundColor(sec2HeaderColors[c - 1])
                .Font.SetFontColor(White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetWrapText(true)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(White);
        }
        ws.Row(r).Height = 28;
        r++;

        // Pre-compute Qty Delivered / Distributed per PRItem
        Dictionary<Guid, decimal> qtyDelivered   = new();
        Dictionary<Guid, decimal> qtyDistributed = new();
        HashSet<Guid> seenDI = new();

        foreach (Delivery delivery in pr.Deliveries)
        {
            foreach (DeliveryItem di in delivery.Items)
            {
                if (!seenDI.Contains(di.Id))
                {
                    seenDI.Add(di.Id);
                    qtyDelivered[di.PRItemId] =
                        qtyDelivered.GetValueOrDefault(di.PRItemId) + di.QtyDelivered;
                }
                foreach (Distribution dist in di.Distributions)
                {
                    qtyDistributed[di.PRItemId] =
                        qtyDistributed.GetValueOrDefault(di.PRItemId) + dist.QtyIssued;
                }
            }
        }

        int sec2DataStart = r;
        foreach (PRItem item in pr.Items.OrderBy(i => i.ItemNo))
        {
            decimal delivered    = qtyDelivered.GetValueOrDefault(item.Id);
            decimal distributed  = qtyDistributed.GetValueOrDefault(item.Id);
            decimal remaining    = Math.Max(0, item.Quantity - delivered);
            bool    isFull       = remaining == 0 && delivered > 0;

            XLColor rowBg = r % 2 == 0 ? White : XLColor.FromHtml("#F8F9FA");

            ws.Cell(r, 1).Value = item.ItemNo;
            ws.Cell(r, 2).Value = item.Description;
            ws.Cell(r, 3).Value = item.StockNo ?? "";
            ws.Cell(r, 4).Value = item.Unit;
            ws.Cell(r, 5).Value = item.Quantity;
            ws.Cell(r, 6).Value = delivered;
            ws.Cell(r, 7).Value = distributed;
            ws.Cell(r, 8).Value = remaining;

            for (int c = 1; c <= 8; c++)
                ws.Cell(r, c).Style.Fill.SetBackgroundColor(rowBg)
                    .Border.SetOutsideBorder(XLBorderStyleValues.Hair)
                    .Border.SetOutsideBorderColor(XLColor.FromHtml("#DEE2E6"));

            // Bold description and qty columns
            ws.Cell(r, 2).Style.Font.SetBold(true);
            ws.Cell(r, 5).Style.Font.SetBold(true)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Blue Qty Delivered
            ws.Cell(r, 6).Style.Font.SetFontColor(delivered > 0 ? Blue : XLColor.FromHtml("#ADB5BD"))
                .Font.SetBold(true)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Amber Qty Distributed
            ws.Cell(r, 7).Style.Font.SetFontColor(distributed > 0 ? Amber : XLColor.FromHtml("#ADB5BD"))
                .Font.SetBold(true)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            // Remaining — green if > 0, red cell if fully delivered
            if (isFull)
                ws.Cell(r, 8).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FDECEA"))
                    .Font.SetFontColor(Red).Font.SetBold(true);
            else
                ws.Cell(r, 8).Style.Font.SetFontColor(XLColor.FromHtml("#1F7A45"))
                    .Font.SetBold(true);
            ws.Cell(r, 8).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

            r++;
        }

        if (!pr.Items.Any())
        {
            ws.Cell(r, 1).Value = "No line items.";
            ws.Range(r, 1, r, 8).Merge();
            ws.Cell(r, 1).Style.Fill.SetBackgroundColor(LightGray)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
            r++;
        }

        r += 2; // gap before Section 3

        // ── Section 3 — Distribution (12 columns A–L, matching UI) ───────────

        ws.Cell(r, 1).Value = "SECTION 3 — DISTRIBUTION";
        ws.Cell(r, 1).Style.Font.SetBold(true).Font.SetFontSize(10)
            .Font.SetFontColor(White)
            .Fill.SetBackgroundColor(DarkGreen);
        ws.Range(r, 1, r, 12).Merge();
        ws.Row(r).Height = 18;
        r++;

        string[] sec3Headers = [
            "Item#", "Description", "Unit", "Qty Delivered",
            "Delivery Ref", "Delivery Date", "Division", "Qty Issued",
            "Issue Ref", "Date Issued", "Issued By", "Remarks"
        ];
        for (int c = 1; c <= 12; c++)
        {
            IXLCell h = ws.Cell(r, c);
            h.Value = sec3Headers[c - 1];
            h.Style.Font.SetBold(true)
                .Fill.SetBackgroundColor(DarkGreen)
                .Font.SetFontColor(White)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetWrapText(true)
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(White);
        }
        ws.Row(r).Height = 28;
        r++;

        int sec3DataStart = r;
        foreach (Delivery delivery in pr.Deliveries.OrderBy(d => d.DeliveryDate))
        {
            foreach (DeliveryItem di in delivery.Items.OrderBy(di => di.PRItem?.ItemNo ?? 0))
            {
                foreach (Distribution dist in di.Distributions)
                {
                    XLColor rowBg = r % 2 == 0 ? White : XLColor.FromHtml("#F8F9FA");

                    ws.Cell(r, 1).Value  = di.PRItem?.ItemNo     ?? 0;
                    ws.Cell(r, 2).Value  = di.PRItem?.Description ?? "";
                    ws.Cell(r, 3).Value  = di.PRItem?.Unit        ?? "";
                    ws.Cell(r, 4).Value  = di.QtyDelivered;
                    ws.Cell(r, 5).Value  = delivery.DeliveryRef;
                    ws.Cell(r, 6).Value  = delivery.DeliveryDate.ToDateTime(TimeOnly.MinValue);
                    ws.Cell(r, 6).Style.NumberFormat.SetFormat("yyyy-MM-dd");
                    ws.Cell(r, 7).Value  = (dist.Division?.Name ?? "");
                    ws.Cell(r, 8).Value  = dist.QtyIssued;
                    ws.Cell(r, 9).Value  = dist.IssueRef;
                    ws.Cell(r, 10).Value = dist.DateIssued.ToDateTime(TimeOnly.MinValue);
                    ws.Cell(r, 10).Style.NumberFormat.SetFormat("yyyy-MM-dd");
                    ws.Cell(r, 11).Value = dist.IssuedBy;
                    ws.Cell(r, 12).Value = dist.Remarks ?? "";

                    for (int c = 1; c <= 12; c++)
                        ws.Cell(r, c).Style.Fill.SetBackgroundColor(rowBg)
                            .Border.SetOutsideBorder(XLBorderStyleValues.Hair)
                            .Border.SetOutsideBorderColor(XLColor.FromHtml("#DEE2E6"));

                    ws.Cell(r, 2).Style.Font.SetBold(true);
                    ws.Cell(r, 8).Style.Font.SetBold(true)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                    r++;
                }
            }
        }

        if (r == sec3DataStart)
        {
            ws.Cell(r, 1).Value = "No distributions recorded.";
            ws.Range(r, 1, r, 12).Merge();
            ws.Cell(r, 1).Style.Fill.SetBackgroundColor(LightGray)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
        }

        // ── Column widths ─────────────────────────────────────────────────────

        // Widths match user's edited file exactly (from openpyxl column_dimensions)
        ws.Column(1) .Width = 6.5;   // A: narrow spacer / Sec2 #
        ws.Column(2) .Width = 36.5;  // B: Sec1 label    / Sec2 Description
        ws.Column(3) .Width = 16.5;  // C: Sec1 value    / Sec2 Stock No.
        ws.Column(4) .Width = 8.5;   // D: spacer        / Sec2 Unit
        ws.Column(5) .Width = 12.5;  // E: spacer        / Sec2 Qty Ordered
        ws.Column(6) .Width = 12.5;  // F: spacer        / Sec2 Qty Delivered
        ws.Column(7) .Width = 14.5;  // G: Sec1 r-label  / Sec2 Qty Distributed
        ws.Column(8) .Width = 12.5;  // H: Sec1 r-value  / Sec2 Remaining
        ws.Column(9) .Width = 22.5;  // I: Total value   / Sec3 cols
        ws.Column(10).Width = 12.5;  // J: Total value½  / Sec3 cols
        ws.Column(9) .Width = 22;   // Issue Ref
        ws.Column(10).Width = 12;   // Date Issued
        ws.Column(11).Width = 20;   // Issued By
        ws.Column(12).Width = 20;   // Remarks

        // Section 1 label width override (already set by pair layout)
        // Row heights for long-text Section 1 rows handled by WrapText auto

        // Freeze top rows so header stays visible on scroll
        ws.SheetView.FreezeRows(2);

        _ = sec2HeaderRow; // suppress unused var warning
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

            // Division is the raw sheet text — resolved to a division id at import time by
            // PurchaseRequestService against the configurable divisions table (v1.2 — RAL-97).

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
                    DivisionName        = divisionRaw,
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
        Value(ROW_DIVISION,           prefilled ? (pr!.Division?.Name ?? "")      : null);
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
                    ws.Cell(dataRow, 5).Value = (dist.Division?.Name ?? "");
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
            "  Default password (after reset): TamarawUser2026!",
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

    /// <summary>
    /// Returns the fiscal quarter label for a given date.
    /// Q1 = Jan–Mar, Q2 = Apr–Jun, Q3 = Jul–Sep, Q4 = Oct–Dec.
    /// Example: 2026-05-15 → "Q2-2026"
    /// </summary>
    private static string ToQuarter(DateOnly date)
    {
        int q = (date.Month - 1) / 3 + 1;
        return $"Q{q}-{date.Year}";
    }

    // ── GenerateWfpReport ─────────────────────────────────────────────────────
    //
    // A4 landscape, 17 columns (A–Q):
    //   A  AIP REF CODE           G  ACCOUNT CODE
    //   B  PROGRAMS/PROJ/ACT      H  OBJECT OF EXPENDITURE
    //   C  RESOURCES NEEDED       I  TOTAL APPROPRIATION
    //   D  RESPONSIBLE UNIT       J  RESERVE (10%)
    //   E  SUCCESS INDICATOR      K  NET APPROPRIATION
    //   F  MEANS OF VERIFICATION  L–O Q1–Q4
    //                             P  QUARTERLY TOTAL
    //                             Q  FUND SOURCE
    //
    // Rows 1–4: Title block.  Rows 5–6: Two-level column headers.
    // Data rows: sector → program → project → activity header → line rows.
    // Ends with program subtotals and a grand-total row.

    /// <inheritdoc />
    public byte[] GenerateWfpReport(WfpExcelReportData data)
    {
        // ── Colour palette ────────────────────────────────────────────────────
        XLColor clrTitle     = XLColor.FromHtml("#155233");
        XLColor clrSubtitle  = XLColor.FromHtml("#1E8449");
        XLColor clrColHeader = XLColor.FromHtml("#1A5276");
        XLColor clrSector    = XLColor.FromHtml("#2C3E50");
        XLColor clrProgram   = XLColor.FromHtml("#196F3D");
        XLColor clrProject   = XLColor.FromHtml("#D5E8D4");
        XLColor clrActivity  = XLColor.FromHtml("#EBF5FB");
        XLColor clrAltLine   = XLColor.FromHtml("#F9F9F9");
        XLColor clrProgTotal = XLColor.FromHtml("#64ec5b");
        XLColor clrGrandTot  = XLColor.FromHtml("#1A5276");
        XLColor clrWhite     = XLColor.White;
        XLColor clrDarkText  = XLColor.FromHtml("#1C2833");
        XLColor clrGrayText  = XLColor.FromHtml("#5D6D7E");

        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet("WFP Report");

        // ── Page setup ────────────────────────────────────────────────────────
        ws.PageSetup.PaperSize        = XLPaperSize.A4Paper;
        ws.PageSetup.PageOrientation  = XLPageOrientation.Landscape;
        ws.PageSetup.FitToPages(1, 0);
        ws.PageSetup.Margins.Left     = 0.3;
        ws.PageSetup.Margins.Right    = 0.3;
        ws.PageSetup.Margins.Top      = 0.5;
        ws.PageSetup.Margins.Bottom   = 0.5;

        // ── Default font ──────────────────────────────────────────────────────
        ws.Style.Font.SetFontName("Arial Narrow");
        ws.Style.Font.SetFontSize(8);

        // ── Column widths ─────────────────────────────────────────────────────
        int[] widths = { 15, 30, 20, 16, 20, 20, 13, 20, 13, 11, 13, 10, 10, 10, 10, 12, 20 };
        for (int c = 0; c < widths.Length; c++)
            ws.Column(c + 1).Width = widths[c];

        int row = 1;

        // ── Title block ───────────────────────────────────────────────────────

        // Row 1: Main title
        ws.Cell(row, 1).Value = "WORK AND FINANCIAL PLAN";
        WfpMerge(ws, row, 1, 17);
        ws.Row(row).Height = 22;
        WfpStyle(ws, row, 1, 17, s => s
            .Font.SetBold(true).Font.SetFontSize(14).Font.SetFontColor(clrWhite)
            .Fill.SetBackgroundColor(clrTitle)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center));
        row++;

        // Row 2: Fiscal year
        ws.Cell(row, 1).Value = $"FISCAL YEAR {data.Wfp.FiscalYear}";
        WfpMerge(ws, row, 1, 17);
        ws.Row(row).Height = 17;
        WfpStyle(ws, row, 1, 17, s => s
            .Font.SetBold(true).Font.SetFontSize(11).Font.SetFontColor(clrWhite)
            .Fill.SetBackgroundColor(clrSubtitle)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        // Row 3: Department
        ws.Cell(row, 1).Value = $"DEPARTMENT:  {data.OfficeName.ToUpper()}";
        WfpMerge(ws, row, 1, 17);
        ws.Row(row).Height = 15;
        WfpStyle(ws, row, 1, 17, s => s
            .Font.SetBold(true).Font.SetFontSize(10)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        // Row 4: Status / generated date
        ws.Cell(row, 1).Value =
            $"Status: {data.Wfp.Status}     |     Generated: {DateTime.Now:MMMM dd, yyyy}";
        WfpMerge(ws, row, 1, 17);
        ws.Row(row).Height = 13;
        WfpStyle(ws, row, 1, 17, s => s
            .Font.SetFontSize(8).Font.SetItalic(true).Font.SetFontColor(clrGrayText)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        row++;

        // ── Column headers rows 5–6 ───────────────────────────────────────────

        // Cols A–H + Q: span both header rows (merged vertically)
        string[] singleHeaders =
        {
            "AIP REF\nCODE",
            "PROGRAMS, PROJECTS\nAND ACTIVITIES",
            "RESOURCES\nNEEDED",
            "RESPONSIBLE\nUNIT / DIVISION",
            "SUCCESS\nINDICATOR",
            "MEANS OF\nVERIFICATION",
            "ACCOUNT\nCODE",
            "OBJECT OF\nEXPENDITURE",
        };
        for (int c = 0; c < 8; c++)
        {
            ws.Cell(row, c + 1).Value = singleHeaders[c];
            ws.Range(row, c + 1, row + 1, c + 1).Merge();
        }

        // Budget group header (I:K)
        ws.Cell(row, 9).Value = "BUDGET AMOUNTS";
        ws.Range(row, 9, row, 11).Merge();

        // Quarterly group header (L:P)
        ws.Cell(row, 12).Value = "QUARTERLY SCHEDULE";
        ws.Range(row, 12, row, 16).Merge();

        // Fund source — span both rows
        ws.Cell(row, 17).Value = "FUND\nSOURCE";
        ws.Range(row, 17, row + 1, 17).Merge();

        int hdrRow1 = row;
        WfpHeaderRowStyle(ws, row, clrColHeader, clrWhite);
        ws.Row(row).Height = 22;
        row++;

        // Sub-headers row (row 6): budget + quarterly columns
        string[] subHeaders =
        {
            "TOTAL\nAPPROP.", "RESERVE\n(10%)", "NET\nAPPROP.",
            "1ST QTR", "2ND QTR", "3RD QTR", "4TH QTR", "TOTAL"
        };
        for (int c = 0; c < 8; c++)
            ws.Cell(row, 9 + c).Value = subHeaders[c];

        WfpHeaderRowStyle(ws, row, clrColHeader, clrWhite);
        ws.Row(row).Height = 22;
        ws.SheetView.FreezeRows(row); // freeze title + headers
        row++;

        // ── Build activity lookup ─────────────────────────────────────────────
        //
        // WfpActivity.AipActivityId → (AipOfficeDto, AipProgramDto, AipProjectDto, AipActivityDto)

        var actLookup = data.Aip.Offices
            .SelectMany(o => o.Programs.SelectMany(p =>
                p.Projects.SelectMany(pr =>
                    pr.Activities.Select(a =>
                        (AipActId: a.Id, Off: o, Prog: p, Proj: pr, Act: a)))))
            .ToDictionary(x => x.AipActId);

        // Ordered flat list of groups (WFP activities that have an AIP context)
        var groups = data.Wfp.Activities
            .Where(wa => actLookup.ContainsKey(wa.AipActivityId))
            .Select(wa =>
            {
                var x = actLookup[wa.AipActivityId];
                return (Sector: x.Off.Sector, Office: x.Off, Program: x.Prog,
                        Project: x.Proj, Activity: x.Act, WfpAct: wa);
            })
            .OrderBy(g => g.Sector)
            .ThenBy(g => g.Office.RefCode)
            .ThenBy(g => g.Program.RefCode)
            .ThenBy(g => g.Project.RefCode)
            .ThenBy(g => g.Activity.RefCode)
            .ToList();

        // ── Data rows ─────────────────────────────────────────────────────────

        decimal grandTotal = 0, grandReserve = 0, grandNet = 0;
        decimal grandQ1 = 0, grandQ2 = 0, grandQ3 = 0, grandQ4 = 0, grandQTot = 0;

        decimal progTotal = 0, progReserve = 0, progNet = 0;
        decimal progQ1 = 0, progQ2 = 0, progQ3 = 0, progQ4 = 0, progQTot = 0;

        string? lastSector       = null;
        string? lastProgramRef   = null;
        string? lastProjectRef   = null;
        string? lastProgramName  = null;

        bool lineIsOdd = true;

        foreach (var g in groups)
        {
            // ── Sector header ──────────────────────────────────────────────
            if (g.Sector != lastSector)
            {
                if (lastProgramRef != null)
                {
                    (progTotal, progReserve, progNet, progQ1, progQ2, progQ3, progQ4, progQTot) =
                        (0, 0, 0, 0, 0, 0, 0, 0);
                }

                lastSector     = g.Sector;
                lastProgramRef = null;
                lastProjectRef = null;

                ws.Cell(row, 1).Value = $"  {g.Sector.ToUpper()} SECTOR";
                WfpMerge(ws, row, 1, 17);
                ws.Row(row).Height = 15;
                WfpStyle(ws, row, 1, 17, s => s
                    .Font.SetBold(true).Font.SetFontSize(9).Font.SetFontColor(clrWhite)
                    .Fill.SetBackgroundColor(clrSector)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
                    .Alignment.SetIndent(1));
                row++;
            }

            // ── Program header ─────────────────────────────────────────────
            if (g.Program.RefCode != lastProgramRef)
            {
                if (lastProgramRef != null)
                {
                    (progTotal, progReserve, progNet, progQ1, progQ2, progQ3, progQ4, progQTot) =
                        (0, 0, 0, 0, 0, 0, 0, 0);
                }

                lastProgramRef  = g.Program.RefCode;
                lastProjectRef  = null;
                lastProgramName = g.Program.Name;

                ws.Cell(row, 1).Value = g.Program.RefCode;
                ws.Cell(row, 2).Value = g.Program.Name.ToUpper();
                WfpMerge(ws, row, 2, 17);
                ws.Row(row).Height = 15;
                WfpStyle(ws, row, 1, 1, s => s
                    .Font.SetBold(true).Font.SetFontSize(8).Font.SetFontColor(clrWhite)
                    .Fill.SetBackgroundColor(clrProgram)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
                WfpStyle(ws, row, 2, 17, s => s
                    .Font.SetBold(true).Font.SetFontSize(8).Font.SetFontColor(clrWhite)
                    .Fill.SetBackgroundColor(clrProgram));
                row++;
            }

            // ── Project header ─────────────────────────────────────────────
            if (g.Project.RefCode != lastProjectRef)
            {
                lastProjectRef = g.Project.RefCode;

                ws.Cell(row, 1).Value = g.Project.RefCode;
                ws.Cell(row, 2).Value = $"  {g.Project.Name}";
                WfpMerge(ws, row, 2, 17);
                ws.Row(row).Height = 14;
                WfpStyle(ws, row, 1, 1, s => s
                    .Font.SetBold(true).Font.SetFontSize(8)
                    .Font.SetFontColor(XLColor.FromHtml("#196F3D"))
                    .Fill.SetBackgroundColor(clrProject));
                WfpStyle(ws, row, 2, 17, s => s
                    .Font.SetBold(true).Font.SetFontSize(8)
                    .Font.SetFontColor(XLColor.FromHtml("#196F3D"))
                    .Fill.SetBackgroundColor(clrProject));
                row++;
            }

            // ── Activity header row ────────────────────────────────────────
            decimal actTotal   = g.WfpAct.Lines.Sum(l => l.TotalAppropriation ?? 0m);
            decimal actReserve = g.WfpAct.Lines.Sum(l => l.ReserveAmount      ?? 0m);
            decimal actNet     = g.WfpAct.Lines.Sum(l => l.NetAppropriation   ?? 0m);
            decimal actQ1      = g.WfpAct.Lines.Sum(l => l.Q1  ?? 0m);
            decimal actQ2      = g.WfpAct.Lines.Sum(l => l.Q2  ?? 0m);
            decimal actQ3      = g.WfpAct.Lines.Sum(l => l.Q3  ?? 0m);
            decimal actQ4      = g.WfpAct.Lines.Sum(l => l.Q4  ?? 0m);
            decimal actQTot    = g.WfpAct.Lines.Sum(l => l.QuarterlyTotal ?? 0m);

            progTotal   += actTotal;   progReserve += actReserve; progNet  += actNet;
            progQ1      += actQ1;      progQ2      += actQ2;      progQ3   += actQ3;
            progQ4      += actQ4;      progQTot    += actQTot;
            grandTotal  += actTotal;   grandReserve+= actReserve; grandNet += actNet;
            grandQ1     += actQ1;      grandQ2     += actQ2;      grandQ3  += actQ3;
            grandQ4     += actQ4;      grandQTot   += actQTot;

            ws.Cell(row, 1).Value = g.Activity.RefCode;
            ws.Cell(row, 2).Value = g.Activity.Name;
            WfpSetNumericCells(ws, row, actTotal, actReserve, actNet,
                actQ1, actQ2, actQ3, actQ4, actQTot);
            ws.Row(row).Height = 14;
            WfpStyle(ws, row, 1, 17, s => s
                .Font.SetBold(true).Font.SetFontSize(8)
                .Fill.SetBackgroundColor(clrActivity));
            ws.Cell(row, 1).Style.Font.SetFontName("Courier New").Font.SetFontSize(7);
            row++;
            lineIsOdd = true;

            // ── Expenditure line rows — grouped PS → MOOE → CO ────────────
            var allLines   = g.WfpAct.Lines.OrderBy(l => l.SortOrder).ToList();
            var typeOrder  = new[] { "PS", "MOOE", "CO" };
            XLColor clrTypeHdr = XLColor.FromHtml("#FFF9C4");
            XLColor clrSubTot  = XLColor.FromHtml("#FFD700");

            foreach (string expType in typeOrder)
            {
                var typeLines = allLines.Where(l => l.ExpenditureType == expType).ToList();
                if (typeLines.Count == 0) continue;

                // Type header row
                ws.Cell(row, 8).Value = expType;
                ws.Row(row).Height = 12;
                WfpStyle(ws, row, 1, 17, s => s.Fill.SetBackgroundColor(clrTypeHdr));
                ws.Cell(row, 8).Style.Font.SetBold(true).Font.SetFontSize(8)
                    .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                row++;

                // Individual lines — col B blank
                decimal subTotal = 0m, subReserve = 0m, subNet = 0m;
                decimal subQ1 = 0m, subQ2 = 0m, subQ3 = 0m, subQ4 = 0m, subQTot = 0m;
                foreach (WfpExpenditureLineDto line in typeLines)
                {
                    XLColor bg = lineIsOdd ? clrWhite : clrAltLine;

                    ws.Cell(row, 3).Value  = line.ResourcesNeeded;
                    ws.Cell(row, 4).Value  = line.ResponsibleUnit;
                    ws.Cell(row, 5).Value  = line.SuccessIndicator;
                    ws.Cell(row, 6).Value  = line.MeansOfVerification;
                    ws.Cell(row, 7).Value  = line.AccountNumberSnapshot;
                    ws.Cell(row, 8).Value  = line.AccountTitleSnapshot;
                    ws.Cell(row, 17).Value = line.FundingSourceNameSnapshot ?? line.FundingSourceSnapshot;

                    decimal lineTotal   = line.TotalAppropriation ?? 0m;
                    decimal lineReserve = line.ApplyReserve ? (line.ReserveAmount ?? 0m) : 0m;
                    decimal lineNet     = line.NetAppropriation ?? 0m;
                    decimal lineQ1      = line.Q1 ?? 0m;
                    decimal lineQ2      = line.Q2 ?? 0m;
                    decimal lineQ3      = line.Q3 ?? 0m;
                    decimal lineQ4      = line.Q4 ?? 0m;
                    decimal lineQTot    = line.QuarterlyTotal ?? 0m;

                    subTotal   += lineTotal;   subReserve += lineReserve; subNet  += lineNet;
                    subQ1      += lineQ1;      subQ2      += lineQ2;      subQ3   += lineQ3;
                    subQ4      += lineQ4;      subQTot    += lineQTot;

                    WfpSetNumericCells(ws, row, lineTotal, lineReserve, lineNet,
                        lineQ1, lineQ2, lineQ3, lineQ4, lineQTot);

                    ws.Row(row).Height = 14;
                    WfpStyle(ws, row, 1, 17, s => s.Fill.SetBackgroundColor(bg));
                    ws.Cell(row, 2).Style.Font.SetItalic(false).Font.SetFontColor(XLColor.FromHtml("#5D6D7E"));
                    ws.Cell(row, 7).Style.Font.SetFontName("Courier New").Font.SetFontSize(7)
                        .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    WfpApplyTextWrap(ws, row, [3, 4, 5, 6]);

                    lineIsOdd = !lineIsOdd;
                    row++;
                }

                // SUB-TOTAL row
                ws.Cell(row, 8).Value = "SUB-TOTAL";
                WfpSetNumericCells(ws, row, subTotal, subReserve, subNet,
                    subQ1, subQ2, subQ3, subQ4, subQTot);
                ws.Row(row).Height = 13;
                WfpStyle(ws, row, 1, 17, s => s
                    .Font.SetBold(true).Font.SetFontSize(8)
                    .Fill.SetBackgroundColor(clrSubTot));
                row++;
            }
        }

        // (programme subtotal removed — totals appear in fund-source grouped blocks below)

        // ── Fund-source grouped total blocks ─────────────────────────────────
        if (groups.Count > 0)
        {
            // Collect all lines from all activities
            var allWfpLines = data.Wfp.Activities
                .SelectMany(a => a.Lines)
                .ToList();

            // For each line resolve its "group color key": look up the fund source
            // display name (or code for old records) in the colors map.
            // null color → no-color group; hex string → that color's group.
            string? GetFsColor(WfpExpenditureLineDto l)
            {
                if (l.FundingSourceId == null) return null;
                return data.FundingSourceColors.TryGetValue(l.FundingSourceId.Value, out string? c) ? c : null;
            }

            // Build ordered list of groups: no-color first, then each distinct hex color
            // in the order first encountered scanning activities top-to-bottom.
            var colorGroupOrder = new List<string?>();   // null = no-color group
            var seenColors      = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            colorGroupOrder.Add(null);                   // no-color group always first
            foreach (WfpExpenditureLineDto l in allWfpLines)
            {
                string? c = GetFsColor(l);
                if (c != null && seenColors.Add(c))
                    colorGroupOrder.Add(c);
            }

            foreach (string? groupColor in colorGroupOrder)
            {
                var groupLines = allWfpLines
                    .Where(l => string.Equals(GetFsColor(l), groupColor,
                                    StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (groupLines.Count == 0) continue;

                XLColor bg = groupColor != null
                    ? XLColor.FromHtml(groupColor)
                    : clrProgTotal;   // default green for no-color group

                // Blank separator row
                ws.Row(row).Height = 6;
                row++;

                // TOTAL - PS / MOOE / CO rows (skip types with zero sum)
                foreach (string expType in new[] { "PS", "MOOE", "CO" })
                {
                    var typeLines = groupLines.Where(l => l.ExpenditureType == expType).ToList();
                    decimal tTotal   = typeLines.Sum(l => l.TotalAppropriation ?? 0m);
                    decimal tReserve = typeLines.Sum(l => l.ReserveAmount      ?? 0m);
                    decimal tNet     = typeLines.Sum(l => l.NetAppropriation   ?? 0m);
                    decimal tQ1      = typeLines.Sum(l => l.Q1 ?? 0m);
                    decimal tQ2      = typeLines.Sum(l => l.Q2 ?? 0m);
                    decimal tQ3      = typeLines.Sum(l => l.Q3 ?? 0m);
                    decimal tQ4      = typeLines.Sum(l => l.Q4 ?? 0m);
                    decimal tQTot    = typeLines.Sum(l => l.QuarterlyTotal ?? 0m);
                    if (tTotal == 0m && tQ1 == 0m && tQ2 == 0m && tQ3 == 0m && tQ4 == 0m) continue;

                    ws.Cell(row, 6).Value = $"TOTAL - {expType}";
                    WfpSetNumericCells(ws, row, tTotal, tReserve, tNet, tQ1, tQ2, tQ3, tQ4, tQTot);
                    ws.Row(row).Height = 13;
                    WfpStyle(ws, row, 1, 17, s => s
                        .Font.SetBold(true).Font.SetFontSize(8)
                        .Fill.SetBackgroundColor(bg)
                        .Alignment.SetVertical(XLAlignmentVerticalValues.Center));
                    ws.Cell(row, 6).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                    row++;
                }

                // GRAND row for this fund-source group
                decimal gTotal   = groupLines.Sum(l => l.TotalAppropriation ?? 0m);
                decimal gReserve = groupLines.Sum(l => l.ReserveAmount      ?? 0m);
                decimal gNet     = groupLines.Sum(l => l.NetAppropriation   ?? 0m);
                decimal gQ1      = groupLines.Sum(l => l.Q1 ?? 0m);
                decimal gQ2      = groupLines.Sum(l => l.Q2 ?? 0m);
                decimal gQ3      = groupLines.Sum(l => l.Q3 ?? 0m);
                decimal gQ4      = groupLines.Sum(l => l.Q4 ?? 0m);
                decimal gQTot    = groupLines.Sum(l => l.QuarterlyTotal ?? 0m);

                ws.Cell(row, 6).Value = "GRAND";
                WfpSetNumericCells(ws, row, gTotal, gReserve, gNet, gQ1, gQ2, gQ3, gQ4, gQTot);
                ws.Row(row).Height = 14;
                WfpStyle(ws, row, 1, 17, s => s
                    .Font.SetBold(true).Font.SetFontSize(9)
                    .Fill.SetBackgroundColor(bg)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Center));
                ws.Cell(row, 6).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                row++;
            }

            // Blank separator before GRAND TOTAL
            ws.Row(row).Height = 6;
            row++;

            // Final GRAND TOTAL
            ws.Cell(row, 6).Value = "GRAND TOTAL";
            WfpSetNumericCells(ws, row, grandTotal, grandReserve, grandNet,
                grandQ1, grandQ2, grandQ3, grandQ4, grandQTot);
            ws.Row(row).Height = 16;
            WfpStyle(ws, row, 1, 17, s => s
                .Font.SetBold(true).Font.SetFontSize(9).Font.SetFontColor(clrWhite)
                .Fill.SetBackgroundColor(clrGrandTot)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center));
            ws.Cell(row, 6).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            row++;
        }

        // ── Empty-data placeholder ────────────────────────────────────────────
        if (groups.Count == 0)
        {
            ws.Cell(row, 1).Value = "No WFP activities have been saved yet.";
            WfpMerge(ws, row, 1, 17);
            WfpStyle(ws, row, 1, 17, s => s
                .Font.SetItalic(true).Font.SetFontColor(clrGrayText)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center));
        }

        // ── Apply outer border to data range ──────────────────────────────────
        if (row > 7)
        {
            ws.Range(hdrRow1, 1, row - 1, 17).Style
                .Border.SetInsideBorder(XLBorderStyleValues.Hair)
                .Border.SetInsideBorderColor(XLColor.FromHtml("#BDC3C7"))
                .Border.SetOutsideBorder(XLBorderStyleValues.Thin)
                .Border.SetOutsideBorderColor(XLColor.FromHtml("#7F8C8D"));
        }

        using MemoryStream ms = new();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ── WFP layout helpers ─────────────────────────────────────────────────────

    private static void WfpMerge(IXLWorksheet ws, int row, int colFrom, int colTo)
        => ws.Range(row, colFrom, row, colTo).Merge();

    private static void WfpStyle(IXLWorksheet ws, int row, int colFrom, int colTo,
        Action<IXLStyle> configure)
        => configure(ws.Range(row, colFrom, row, colTo).Style);

    private static void WfpHeaderRowStyle(IXLWorksheet ws, int row, XLColor bg, XLColor fg)
        => ws.Range(row, 1, row, 17).Style
            .Font.SetBold(true).Font.SetFontSize(8).Font.SetFontColor(fg)
            .Fill.SetBackgroundColor(bg)
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center)
            .Alignment.SetWrapText(true);

    private static void WfpSetNumericCells(IXLWorksheet ws, int row,
        decimal total, decimal reserve, decimal net,
        decimal q1, decimal q2, decimal q3, decimal q4, decimal qTot)
    {
        const string fmt = "#,##0.00;-#,##0.00;\"-\"";
        void Set(int col, decimal v)
        {
            ws.Cell(row, col).Value = v;
            ws.Cell(row, col).Style
                .NumberFormat.SetFormat(fmt)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
        }
        Set(9, total); Set(10, reserve); Set(11, net);
        Set(12, q1);   Set(13, q2);     Set(14, q3); Set(15, q4); Set(16, qTot);
    }

    private static void WfpApplyTextWrap(IXLWorksheet ws, int row, int[] cols)
    {
        foreach (int col in cols)
            ws.Cell(row, col).Style.Alignment.SetWrapText(true);
    }

    private static int WriteProgramSubtotal(IXLWorksheet ws, int row,
        string programName, XLColor bg, XLColor fg,
        decimal total, decimal reserve, decimal net,
        decimal q1, decimal q2, decimal q3, decimal q4, decimal qTot)
    {
        ws.Cell(row, 6).Value = "PROGRAMME TOTAL";
        WfpSetNumericCells(ws, row, total, reserve, net, q1, q2, q3, q4, qTot);
        ws.Row(row).Height = 14;
        ws.Range(row, 1, row, 17).Style
            .Font.SetBold(true).Font.SetFontSize(8).Font.SetFontColor(fg)
            .Fill.SetBackgroundColor(bg)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        ws.Cell(row, 6).Style
            .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left)
            .Alignment.SetVertical(XLAlignmentVerticalValues.Center);
        return row + 1;
    }
}
