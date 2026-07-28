using ClosedXML.Excel;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="PpmpReportExcelService"/> (RAL-184). Structural — proves the
/// programmatic build produces a valid workbook with the right sheets, hierarchy labels, item
/// rows, section totals, and no formulas, against a realistic multi-fund
/// <see cref="PpmpReportDto"/>. Not a byte-for-byte match of the province form (that's live QA).
/// Deliberately does NOT assert styling (fills/widths) so the tests survive colour iteration.
/// </summary>
public sealed class PpmpReportExcelServiceTests
{
    private readonly PpmpReportExcelService _sut = new();

    private static PpmpReportItemDto Item(string desc, string? stock, decimal est) =>
        new(stock, stock is null ? null : "Office Supplies", desc, "ream", 100m, 5m, est,
            2m, est * 0.4m, 1m, est * 0.2m, 1m, est * 0.2m, 1m, est * 0.2m);

    private static PpmpReportDto SampleReport()
    {
        PpmpReportItemDto linked = Item("Bond paper A4 80gsm", "OSE-1714343441", 1000m);
        PpmpReportItemDto free = Item("Free-typed thing", null, 50m);

        PpmpReportAccountDto account = new("5-02-03-010", "Office Supplies Expenses",
            new[] { linked, free }, 1050m);
        PpmpReportActivityDto activity = new("1000-000-1-01-010-002-001-001",
            "Procurement of office supplies", new[] { account }, 1050m);
        PpmpReportProjectDto project = new("1000-000-1-01-010-002-001",
            "Property and Supply Project", new[] { activity }, 1050m);
        PpmpReportProgramDto program = new("1000-000-1-01-010-002",
            "Administrative Management Program", new[] { project }, 1050m);

        PpmpReportFundSourceDto gf = new("GENERAL FUND", new[] { program }, 1050m);

        PpmpReportItemDto ldrrmItem = Item("First Aid Kit", "MDLS-0001", 800m);
        PpmpReportAccountDto ldrrmAcct = new("5-02-03-990", "Other Supplies",
            new[] { ldrrmItem }, 800m);
        PpmpReportActivityDto ldrrmAct = new("1000-000-1-01-010-007-001-001",
            "Disaster preparedness", new[] { ldrrmAcct }, 800m);
        PpmpReportProjectDto ldrrmPrj = new("1000-000-1-01-010-007-001", "DRRM Project",
            new[] { ldrrmAct }, 800m);
        PpmpReportProgramDto ldrrmPgm = new("1000-000-1-01-010-007", "DRRM Program",
            new[] { ldrrmPrj }, 800m);
        PpmpReportFundSourceDto ldrrm = new("5% LDRRM FUND", new[] { ldrrmPgm }, 800m);

        return new PpmpReportDto(2027, "PPDO", "Provincial Planning and Development Office",
            DivisionName: null, new[] { gf, ldrrm });
    }

    [Fact]
    public void Export_ReturnsNonEmptyByteArray()
    {
        Assert.NotEmpty(_sut.Export(SampleReport()));
    }

    [Fact]
    public void Export_ContainsOneSheetPerFundSource()
    {
        PpmpReportDto report = SampleReport();
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(report)));

        Assert.Equal(report.FundSourceReports.Count, wb.Worksheets.Count);
        Assert.Contains(wb.Worksheets, ws => ws.Name.Contains("GENERAL FUND"));
        Assert.Contains(wb.Worksheets, ws => ws.Name.Contains("LDRRM"));
    }

    [Fact]
    public void Export_WritesHeaderHierarchyItemsAndTotals_NoFormulas()
    {
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(SampleReport())));
        IXLWorksheet ws = wb.Worksheets.First(); // GENERAL FUND

        string allText = string.Join(" ", ws.CellsUsed().Select(c => c.GetString()));
        Assert.Contains("PROJECT PROCUREMENT MANAGEMENT PLAN (PPMP)", allText);
        Assert.Contains("END-USER/UNIT: Provincial Planning and Development Office", allText);
        Assert.Contains("Charged to: GENERAL FUND", allText);
        Assert.Contains("Administrative Management Program", allText);
        Assert.Contains("Property and Supply Project", allText);
        Assert.Contains("Procurement of office supplies", allText);
        Assert.Contains("5-02-03-010 — Office Supplies Expenses", allText);
        Assert.Contains("Bond paper A4 80gsm", allText);
        Assert.Contains("OSE-1714343441", allText);        // live stock card no.
        Assert.Contains("TOTAL — GENERAL FUND", allText);
        Assert.Contains("Prepared by:", allText);
        Assert.Contains("Noted by:", allText);

        // Values-not-formulas.
        Assert.All(ws.CellsUsed(), cell => Assert.False(cell.HasFormula));
    }

    [Fact]
    public void Export_HasNoModeOfProcurementColumn()
    {
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(SampleReport())));
        IXLWorksheet ws = wb.Worksheets.First();
        string allText = string.Join(" ", ws.CellsUsed().Select(c => c.GetString()));
        Assert.DoesNotContain("Mode of Proc", allText);
    }

    [Fact]
    public void Export_AipRefCode_IsInColumnB_ItemNoColumnBlankOnSectionRows()
    {
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(SampleReport())));
        IXLWorksheet ws = wb.Worksheets.First();

        // The program ref code must live in column B (AIP Reference Code), not column A (Item No.).
        IXLCell refCell = ws.CellsUsed().First(c => c.GetString() == "1000-000-1-01-010-002");
        Assert.Equal(2, refCell.Address.ColumnNumber);
        // Its Item No. cell (column A, same row) is blank.
        Assert.True(string.IsNullOrEmpty(ws.Cell(refCell.Address.RowNumber, 1).GetString()));
    }

    [Fact]
    public void Export_ItemRows_AreNumberedInColumnA()
    {
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(SampleReport())));
        IXLWorksheet ws = wb.Worksheets.First();

        // The item "Bond paper A4 80gsm" is in column C; its row's column A holds item number 1.
        IXLCell descCell = ws.CellsUsed().First(c => c.GetString() == "Bond paper A4 80gsm");
        Assert.Equal(3, descCell.Address.ColumnNumber);
        Assert.Equal(1, ws.Cell(descCell.Address.RowNumber, 1).GetValue<int>());
    }

    [Fact]
    public void Export_AccountAndFundTotals_MatchDtoExactly()
    {
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(SampleReport())));
        IXLWorksheet ws = wb.Worksheets.First();

        // Account total (Q11 = sum of items) sits in the Est. Budget column (I=9) on the account row.
        IXLCell accountLabel = ws.CellsUsed().First(c => c.GetString().StartsWith("5-02-03-010"));
        Assert.Equal(1050m, ws.Cell(accountLabel.Address.RowNumber, 9).GetValue<decimal>());

        // Fund total in the same column on the "TOTAL — …" row.
        IXLCell fundLabel = ws.CellsUsed().First(c => c.GetString() == "TOTAL — GENERAL FUND");
        Assert.Equal(1050m, ws.Cell(fundLabel.Address.RowNumber, 9).GetValue<decimal>());
    }

    [Fact]
    public void Export_EmptyReport_StillProducesOneSheet()
    {
        PpmpReportDto empty = new(2027, "PPDO", "Provincial Planning and Development Office", null, []);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(empty)));
        Assert.Single(wb.Worksheets);
    }

    [Fact]
    public void Export_DivisionScoped_HeaderShowsDivision()
    {
        PpmpReportDto report = SampleReport() with { DivisionName = "Sectoral Planning Division" };
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(_sut.Export(report)));
        string allText = string.Join(" ", wb.Worksheets.First().CellsUsed().Select(c => c.GetString()));
        Assert.Contains("Provincial Planning and Development Office — Sectoral Planning Division", allText);
    }
}
