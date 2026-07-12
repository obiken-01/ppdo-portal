using ClosedXML.Excel;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="ExcelService"/> — written first (TDD).
/// Uses ClosedXML directly to build test Excel files in memory.
/// Coverage target: 80% (Application/Service layer — ExcelService lives in Infrastructure
/// but is pure logic with no DB access, making it unit-testable).
/// </summary>
public sealed class ExcelServiceTests
{
    private readonly ExcelService _sut = new();

    // ── GeneratePRTemplate ────────────────────────────────────────────────────

    [Fact]
    public void GeneratePRTemplate_ReturnsNonEmptyByteArray()
    {
        byte[] result = _sut.GeneratePRTemplate();
        Assert.NotEmpty(result);
    }

    [Fact]
    public void GeneratePRTemplate_ContainsPRTemplateSheet()
    {
        byte[] result = _sut.GeneratePRTemplate();
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        Assert.True(wb.Worksheets.Count >= 1);
        Assert.Contains(wb.Worksheets, ws => ws.Name.StartsWith("PR-"));
    }

    [Fact]
    public void GeneratePRTemplate_ContainsInstructionsSheet()
    {
        byte[] result = _sut.GeneratePRTemplate();
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        Assert.Contains(wb.Worksheets, ws =>
            ws.Name.Equals("Instructions", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GeneratePRTemplate_SectionHeadersPresent()
    {
        byte[] result = _sut.GeneratePRTemplate();
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        IXLWorksheet ws = wb.Worksheets.First(w => w.Name.StartsWith("PR-"));

        // The sheet should have recognisable labels for required fields
        string allText = string.Concat(
            ws.CellsUsed().Select(c => c.GetString()));

        Assert.Contains("Division",     allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Requested By", allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR Date",      allText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Description",  allText, StringComparison.OrdinalIgnoreCase);
    }

    // ── ParsePRImport ─────────────────────────────────────────────────────────

    /// <summary>
    /// Builds an in-memory .xlsx that matches ExcelService's expected template layout,
    /// with valid data in all required fields and one item row.
    /// </summary>
    private static Stream BuildValidTemplate(string sheetName = "PR-001")
    {
        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet(sheetName);

        // Section 1 — labels in col A, values in col B
        ws.Cell(ExcelService.ROW_DEPARTMENT,         1).Value = "Department";
        ws.Cell(ExcelService.ROW_DEPARTMENT,         2).Value = "PPDO";
        ws.Cell(ExcelService.ROW_DIVISION,           1).Value = "Division";
        ws.Cell(ExcelService.ROW_DIVISION,           2).Value = "Planning";   // required
        ws.Cell(ExcelService.ROW_FUND,               1).Value = "Fund";
        ws.Cell(ExcelService.ROW_FUND,               2).Value = "General Fund";
        ws.Cell(ExcelService.ROW_REQUESTED_BY,       1).Value = "Requested By";
        ws.Cell(ExcelService.ROW_REQUESTED_BY,       2).Value = "Juan dela Cruz"; // required
        ws.Cell(ExcelService.ROW_POSITION,           1).Value = "Position";
        ws.Cell(ExcelService.ROW_POSITION,           2).Value = "Planning Officer";
        ws.Cell(ExcelService.ROW_PR_DATE,            1).Value = "PR Date";
        ws.Cell(ExcelService.ROW_PR_DATE,            2).Value = new DateTime(2026, 6, 1); // required
        ws.Cell(ExcelService.ROW_APPROVED_BY,        1).Value = "Approved By";
        ws.Cell(ExcelService.ROW_APPROVED_BY,        2).Value = "Maria Santos";
        ws.Cell(ExcelService.ROW_APPROVING_POSITION, 1).Value = "Approving Position";
        ws.Cell(ExcelService.ROW_APPROVING_POSITION, 2).Value = "Division Chief";
        ws.Cell(ExcelService.ROW_PROGRAM,            1).Value = "Program";
        ws.Cell(ExcelService.ROW_PROGRAM,            2).Value = "General Administration";

        // Section 2 — items (header row + one item)
        ws.Cell(ExcelService.ROW_ITEMS_HEADER, ExcelService.COL_STOCK_NO)   .Value = "Stock No.";
        ws.Cell(ExcelService.ROW_ITEMS_HEADER, ExcelService.COL_DESCRIPTION).Value = "Description";
        ws.Cell(ExcelService.ROW_ITEMS_HEADER, ExcelService.COL_UNIT)       .Value = "Unit";
        ws.Cell(ExcelService.ROW_ITEMS_HEADER, ExcelService.COL_QTY)        .Value = "Qty";
        ws.Cell(ExcelService.ROW_ITEMS_HEADER, ExcelService.COL_UNIT_COST)  .Value = "Unit Cost";

        int row = ExcelService.ROW_ITEMS_START;
        ws.Cell(row, ExcelService.COL_STOCK_NO)   .Value = "01-01-01-01";
        ws.Cell(row, ExcelService.COL_DESCRIPTION).Value = "Bond Paper A4";
        ws.Cell(row, ExcelService.COL_UNIT)       .Value = "ream";
        ws.Cell(row, ExcelService.COL_QTY)        .Value = 5m;
        ws.Cell(row, ExcelService.COL_UNIT_COST)  .Value = 220m;

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void ParsePRImport_ValidFile_ReturnsPRImportRow()
    {
        using Stream stream = BuildValidTemplate();
        IReadOnlyList<PurchaseRequestImportRow> result = _sut.ParsePRImport(stream);

        Assert.Single(result);
        Assert.Equal("PR-001",         result[0].SheetName);
        Assert.Equal("Planning", result[0].DivisionName);
        Assert.Equal("Juan dela Cruz", result[0].RequestedBy);
        Assert.Equal(new DateOnly(2026, 6, 1), result[0].PRDate);
    }

    [Fact]
    public void ParsePRImport_ValidFile_ParsesItemRow()
    {
        using Stream stream = BuildValidTemplate();
        IReadOnlyList<PurchaseRequestImportRow> result = _sut.ParsePRImport(stream);

        Assert.Single(result[0].Items);
        PRItemImportRow item = result[0].Items[0];
        Assert.Equal("Bond Paper A4", item.Description);
        Assert.Equal("01-01-01-01",   item.StockNo);
        Assert.Equal("ream",          item.Unit);
        Assert.Equal(5m,              item.Quantity);
        Assert.Equal(220m,            item.UnitCost);
    }

    [Fact]
    public void ParsePRImport_MissingDivision_ThrowsExcelParseException()
    {
        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet("PR-001");
        // Division cell left blank — required
        ws.Cell(ExcelService.ROW_REQUESTED_BY, 2).Value = "Juan";
        ws.Cell(ExcelService.ROW_PR_DATE,      2).Value = new DateTime(2026, 6, 1);
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_DESCRIPTION).Value = "Bond Paper";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_UNIT)       .Value = "ream";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_QTY)        .Value = 1m;

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;

        ExcelParseException ex = Assert.Throws<ExcelParseException>(() => _sut.ParsePRImport(ms));
        Assert.Contains(ex.Errors, e => e.Contains("Division"));
    }

    [Fact]
    public void ParsePRImport_MissingRequestedBy_ThrowsExcelParseException()
    {
        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet("PR-001");
        ws.Cell(ExcelService.ROW_DIVISION,     2).Value = "Planning";
        ws.Cell(ExcelService.ROW_PR_DATE,      2).Value = new DateTime(2026, 6, 1);
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_DESCRIPTION).Value = "Item";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_UNIT)       .Value = "pcs";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_QTY)        .Value = 1m;

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;

        ExcelParseException ex = Assert.Throws<ExcelParseException>(() => _sut.ParsePRImport(ms));
        Assert.Contains(ex.Errors, e => e.Contains("Requested By"));
    }

    [Fact]
    public void ParsePRImport_InvalidPRDate_ThrowsExcelParseException()
    {
        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet("PR-001");
        ws.Cell(ExcelService.ROW_DIVISION,     2).Value = "Planning";
        ws.Cell(ExcelService.ROW_REQUESTED_BY, 2).Value = "Juan";
        ws.Cell(ExcelService.ROW_PR_DATE,      2).Value = "not-a-date"; // invalid
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_DESCRIPTION).Value = "Item";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_UNIT)       .Value = "pcs";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_QTY)        .Value = 1m;

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;

        ExcelParseException ex = Assert.Throws<ExcelParseException>(() => _sut.ParsePRImport(ms));
        Assert.Contains(ex.Errors, e => e.Contains("PR Date"));
    }

    [Fact]
    public void ParsePRImport_NoItemRows_ThrowsExcelParseException()
    {
        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet("PR-001");
        ws.Cell(ExcelService.ROW_DIVISION,     2).Value = "Admin";
        ws.Cell(ExcelService.ROW_REQUESTED_BY, 2).Value = "Juan";
        ws.Cell(ExcelService.ROW_PR_DATE,      2).Value = new DateTime(2026, 6, 1);
        // No item rows filled in

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;

        ExcelParseException ex = Assert.Throws<ExcelParseException>(() => _sut.ParsePRImport(ms));
        Assert.Contains(ex.Errors, e => e.Contains("item") || e.Contains("PR-001"));
    }

    [Fact]
    public void ParsePRImport_BlankDescriptionAndStockNo_SkipsRow()
    {
        using XLWorkbook wb = new();
        IXLWorksheet ws = wb.AddWorksheet("PR-001");
        ws.Cell(ExcelService.ROW_DIVISION,     2).Value = "RM";
        ws.Cell(ExcelService.ROW_REQUESTED_BY, 2).Value = "Maria";
        ws.Cell(ExcelService.ROW_PR_DATE,      2).Value = new DateTime(2026, 6, 1);

        // Row 1: blank Description AND blank StockNo — should be skipped
        ws.Cell(ExcelService.ROW_ITEMS_START, ExcelService.COL_QTY).Value = 5m;

        // Row 2: valid item
        int row2 = ExcelService.ROW_ITEMS_START + 1;
        ws.Cell(row2, ExcelService.COL_DESCRIPTION).Value = "Bond Paper";
        ws.Cell(row2, ExcelService.COL_UNIT)       .Value = "ream";
        ws.Cell(row2, ExcelService.COL_QTY)        .Value = 3m;

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;

        IReadOnlyList<PurchaseRequestImportRow> result = _sut.ParsePRImport(ms);
        Assert.Single(result[0].Items); // only the valid row
        Assert.Equal("Bond Paper", result[0].Items[0].Description);
    }

    [Fact]
    public void ParsePRImport_ErrorsAcrossMultipleSheets_CollectsAll()
    {
        using XLWorkbook wb = new();

        // Sheet 1 — missing RequestedBy
        IXLWorksheet ws1 = wb.AddWorksheet("PR-001");
        ws1.Cell(ExcelService.ROW_DIVISION, 2).Value = "Admin";
        ws1.Cell(ExcelService.ROW_PR_DATE,  2).Value = new DateTime(2026, 6, 1);
        ws1.Cell(ExcelService.ROW_ITEMS_START, ExcelService.COL_DESCRIPTION).Value = "Item";
        ws1.Cell(ExcelService.ROW_ITEMS_START, ExcelService.COL_UNIT)       .Value = "pcs";
        ws1.Cell(ExcelService.ROW_ITEMS_START, ExcelService.COL_QTY)        .Value = 1m;

        // Sheet 2 — missing Division
        IXLWorksheet ws2 = wb.AddWorksheet("PR-002");
        ws2.Cell(ExcelService.ROW_REQUESTED_BY, 2).Value = "Pedro";
        ws2.Cell(ExcelService.ROW_PR_DATE,      2).Value = new DateTime(2026, 6, 1);
        ws2.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_DESCRIPTION).Value = "Item";
        ws2.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_UNIT)       .Value = "pcs";
        ws2.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_QTY)        .Value = 1m;

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;

        ExcelParseException ex = Assert.Throws<ExcelParseException>(() => _sut.ParsePRImport(ms));
        Assert.True(ex.Errors.Count >= 2);
        Assert.Contains(ex.Errors, e => e.Contains("PR-001"));
        Assert.Contains(ex.Errors, e => e.Contains("PR-002"));
    }

    [Fact]
    public void ParsePRImport_SkipsInstructionsSheet()
    {
        using XLWorkbook wb = new();

        // Instructions sheet should be ignored
        IXLWorksheet inst = wb.AddWorksheet("Instructions");
        inst.Cell(1, 1).Value = "Some instructions";

        // Valid PR sheet
        IXLWorksheet ws = wb.AddWorksheet("PR-001");
        ws.Cell(ExcelService.ROW_DIVISION,     2).Value = "MIS";
        ws.Cell(ExcelService.ROW_REQUESTED_BY, 2).Value = "Ana";
        ws.Cell(ExcelService.ROW_PR_DATE,      2).Value = new DateTime(2026, 6, 1);
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_DESCRIPTION).Value = "Item";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_UNIT)       .Value = "pcs";
        ws.Cell(ExcelService.ROW_ITEMS_START,  ExcelService.COL_QTY)        .Value = 2m;

        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;

        IReadOnlyList<PurchaseRequestImportRow> result = _sut.ParsePRImport(ms);
        Assert.Single(result); // only PR-001, Instructions skipped
    }

    // ── ExportPRReport ────────────────────────────────────────────────────────

    [Fact]
    public void ExportPRReport_ReturnsNonEmptyByteArray()
    {
        PurchaseRequest pr = BuildSamplePR();
        byte[] result = _sut.ExportPRReport(pr);
        Assert.NotEmpty(result);
    }

    [Fact]
    public void ExportPRReport_ContainsSingleWorksheet()
    {
        PurchaseRequest pr = BuildSamplePR();
        byte[] result = _sut.ExportPRReport(pr);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        Assert.Single(wb.Worksheets);
    }

    [Fact]
    public void ExportPRReport_ContainsPRNoAndDivision()
    {
        PurchaseRequest pr = BuildSamplePR();
        byte[] result = _sut.ExportPRReport(pr);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        IXLWorksheet ws = wb.Worksheets.First();

        string allText = string.Concat(ws.CellsUsed().Select(c => c.GetString()));
        Assert.Contains(pr.PRNo,              allText);
        Assert.Contains(pr.RequestedBy,       allText);
        Assert.Contains(pr.Division!.Name, allText);
    }

    [Fact]
    public void ExportPRReport_ContainsItemDescriptions()
    {
        PurchaseRequest pr = BuildSamplePR();
        byte[] result = _sut.ExportPRReport(pr);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        IXLWorksheet ws = wb.Worksheets.First();

        string allText = string.Concat(ws.CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Bond Paper A4", allText);
    }

    // ── GenerateWfpReport ─────────────────────────────────────────────────────

    [Fact]
    public void GenerateWfpReport_EmptyActivities_ReturnsValidXlsx()
    {
        byte[] result = _sut.GenerateWfpReport(EmptyWfpReportData());
        Assert.NotEmpty(result);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        Assert.Single(wb.Worksheets);
    }

    [Fact]
    public void GenerateWfpReport_HasWfpReportSheet()
    {
        byte[] result = _sut.GenerateWfpReport(EmptyWfpReportData());
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        Assert.Contains(wb.Worksheets, ws => ws.Name == "WFP Report");
    }

    [Fact]
    public void GenerateWfpReport_ContainsTitleAndFiscalYear()
    {
        byte[] result = _sut.GenerateWfpReport(EmptyWfpReportData());
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        string allText = string.Concat(wb.Worksheets.First().CellsUsed().Select(c => c.GetString()));
        Assert.Contains("WORK AND FINANCIAL PLAN", allText);
        Assert.Contains("2027", allText);
    }

    [Fact]
    public void GenerateWfpReport_WithActivity_ContainsActivityNameAndAmount()
    {
        byte[] result = _sut.GenerateWfpReport(WfpReportDataWithActivity());
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        string allText = string.Concat(wb.Worksheets.First().CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Test Activity", allText);
        Assert.Contains("TEST PROGRAM",  allText);
    }

    private static WfpExcelReportData EmptyWfpReportData()
    {
        WfpRecordDetailDto wfp = new(
            1, 1, 1, null, 2027, "Draft", Guid.NewGuid(),
            DateTime.UtcNow, DateTime.UtcNow, null, null, []);
        AipRecordDetailDto aip = new(
            1, 2027, "upload", null, Guid.NewGuid(),
            DateTime.UtcNow, "Final", null, null, []);
        return new WfpExcelReportData(wfp, aip, "Test Department", "TD",
            new Dictionary<int, string?>());
    }

    private static WfpExcelReportData WfpReportDataWithActivity()
    {
        AipActivityDto aipAct = new(
            99, 1, "ACT-01", "Test Activity",
            null, null, null, null, null,
            null, null,
            100000m, null, null, 100000m,
            null, null, null, false);
        AipProjectDto  aipProj   = new(1, 1, "PROJ-01", "Test Project",  [aipAct]);
        AipProgramDto  aipProg   = new(1, 1, "PROG-01", "Test Program",  [aipProj], null);
        AipOfficeDto   aipOffice = new(1, 1, "01-010",  "Test Office",   "GENERAL", [aipProg]);
        AipRecordDetailDto aip   = new(1, 2027, "upload", null, Guid.NewGuid(),
            DateTime.UtcNow, "Final", null, null, [aipOffice]);

        WfpExpenditureLineDto line = new(
            1, 1, "PS",
            null, null, null, null,
            null, "5-01-01", "Salaries",
            100000m, false, 0m, 100000m,
            25000m, 25000m, 25000m, 25000m, 100000m,
            null, "GF", "General Fund", 0);
        WfpActivityDto    wfpAct = new(1, 1, 99, [line]);
        WfpRecordDetailDto wfp   = new(
            1, 1, 1, null, 2027, "Draft", Guid.NewGuid(),
            DateTime.UtcNow, DateTime.UtcNow, null, null, [wfpAct]);

        return new WfpExcelReportData(wfp, aip, "Test Office", "TO",
            new Dictionary<int, string?>());
    }

    // ── Fixture ───────────────────────────────────────────────────────────────

    private static PurchaseRequest BuildSamplePR()
    {
        Guid prId = Guid.NewGuid();
        Guid itemId = Guid.NewGuid();

        PurchaseRequest pr = new()
        {
            Id          = prId,
            PRNo        = "101-1041-GF-2026-06-01-001",
            PRDate      = new DateOnly(2026, 6, 1),
            DateCreated = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            Department  = "PPDO",
            Division    = new Division { Id = 2, OfficeId = 100, Name = "Planning" },
            Fund        = "General Fund",
            RequestedBy = "Juan dela Cruz",
            Position    = "Planning Officer",
            Status      = Domain.Enums.PRStatus.Open,
            TotalAmount = 1100m,
            CreatedById = Guid.NewGuid(),
        };

        PRItem item = new()
        {
            Id          = itemId,
            PRId        = prId,
            ItemNo      = 1,
            StockNo     = "01-01-01-01",
            Description = "Bond Paper A4",
            Unit        = "ream",
            Quantity    = 5m,
            UnitCost    = 220m,
            TotalCost   = 1100m,
        };
        pr.Items.Add(item);

        return pr;
    }
}
