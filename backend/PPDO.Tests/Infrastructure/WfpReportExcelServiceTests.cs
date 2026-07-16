using ClosedXML.Excel;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="WfpReportExcelService"/> (RAL-159, v1.4.4). Exercises the
/// programmatic row/merge-building approach against a realistic multi-activity, multi-fund
/// <see cref="WfpReportDto"/> — the goal is to prove the computed vertical-merge logic (the
/// technical risk this ticket was created to de-risk) produces a structurally valid workbook,
/// not to byte-for-byte match WFP-NEW.xlsx (that's RAL-160's live verification job).
/// </summary>
public sealed class WfpReportExcelServiceTests
{
    private readonly WfpReportExcelService _sut = new();

    private static WfpReportAmountsDto Amt(decimal total) =>
        new(total, total * 0.1m, total * 0.9m, total * 0.225m, total * 0.225m, total * 0.225m, total * 0.225m, total * 0.9m);

    private static WfpReportDto SampleReport()
    {
        WfpReportRowDto row1 = new("GENERAL PUBLIC SERVICES", "Non-procurement", "5-01-01-010", "Salaries", Amt(100_000));
        WfpReportRowDto row2 = new("GENERAL PUBLIC SERVICES", "Non-procurement", "5-01-01-020", "PERA", Amt(20_000));

        WfpReportExpenseClassGroupDto psGroup = new("PS", "PERSONAL SERVICES",
            new[] { row1, row2 }, Amt(120_000));

        WfpReportActivityDto activity = new(
            "1000-000-1-01-007-001-001-001", "Conduct quarterly training",
            IsCreation: false, new[] { psGroup }, Amt(120_000));

        WfpReportProjectDto project = new(
            "1000-000-1-01-007-001-001", "Capacity building project",
            new[] { activity }, Amt(120_000));

        WfpReportProgramDto program = new(
            "1000-000-1-01-007-001", "General administration program",
            new[] { project }, Amt(120_000));

        WfpReportFunctionBandSectionDto band = new(
            "CORE", "CORE FUNCTIONS", new[] { program });

        WfpReportBreakdownDto breakdown = new(
            PersonalServices: Amt(120_000),
            MooeExcludingCreation: WfpReportAmountsDto.Zero,
            CapitalOutlay: WfpReportAmountsDto.Zero,
            PersonalServicesCreation: WfpReportAmountsDto.Zero,
            MooeCreation: WfpReportAmountsDto.Zero,
            GrandTotal: Amt(120_000));

        WfpReportFundSourceDto fund = new("General Fund", new[] { band }, breakdown);

        return new WfpReportDto(2027, "PPDO", "Provincial Planning and Development Office",
            0.10m, new[] { fund });
    }

    [Fact]
    public void Export_ReturnsNonEmptyByteArray()
    {
        byte[] result = _sut.Export(SampleReport());
        Assert.NotEmpty(result);
    }

    [Fact]
    public void Export_ContainsOneSheetPerFundSource()
    {
        WfpReportDto report = SampleReport();
        byte[] result = _sut.Export(report);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));

        Assert.Equal(report.FundSourceReports.Count, wb.Worksheets.Count);
        Assert.Contains(wb.Worksheets, ws => ws.Name.Contains("General Fund"));
    }

    [Fact]
    public void Export_WritesHierarchyLabelsAndComputedValues_NoFormulas()
    {
        byte[] result = _sut.Export(SampleReport());
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        IXLWorksheet ws = wb.Worksheets.First();

        string allText = string.Join(" ", ws.CellsUsed().Select(c => c.GetString()));
        Assert.Contains("CORE FUNCTIONS", allText);
        Assert.Contains("General administration program", allText);
        Assert.Contains("Capacity building project", allText);
        Assert.Contains("Conduct quarterly training", allText);
        Assert.Contains("PERSONAL SERVICES", allText);
        Assert.Contains("SUB-TOTAL", allText);
        Assert.Contains("ACTIVITY GRAND TOTAL", allText);
        Assert.Contains("PROJECT GRAND TOTAL", allText);
        Assert.Contains("PROGRAM GRAND TOTAL", allText);
        Assert.Contains("GRAND-TOTAL", allText);

        // Every cell holding a value must be a literal, never a formula (values-not-formulas decision).
        Assert.All(ws.CellsUsed(), cell => Assert.False(cell.HasFormula));
    }

    [Fact]
    public void Export_ActivityRefAndTitle_AreMergedAcrossTheirBlock()
    {
        byte[] result = _sut.Export(SampleReport());
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        IXLWorksheet ws = wb.Worksheets.First();

        // The activity's PS group spans: expense-class header + 2 lines + sub-total = 4 rows.
        // Column B (ref) and E (activity title) must each be merged across exactly that block —
        // below row 9 to exclude the column-header block's own 2-row vertical merges (B7:B8 etc).
        var refMerge = ws.MergedRanges.FirstOrDefault(r =>
            r.FirstColumn().ColumnNumber() == 2 && r.RangeAddress.FirstAddress.RowNumber > 9);
        Assert.NotNull(refMerge);
        Assert.Equal(4, refMerge!.RowCount());

        var titleMerge = ws.MergedRanges.FirstOrDefault(r =>
            r.FirstColumn().ColumnNumber() == 5 && r.RangeAddress.FirstAddress.RowNumber > 9);
        Assert.NotNull(titleMerge);
        Assert.Equal(refMerge.RowCount(), titleMerge!.RowCount());
    }

    [Fact]
    public void Export_FkColumns_AreBlank()
    {
        byte[] result = _sut.Export(SampleReport());
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        IXLWorksheet ws = wb.Worksheets.First();

        // Header row labels (row 7) legitimately occupy F–K — only the data area (row > 9,
        // where the activity block's F–K cells live, blank by design) must be checked.
        for (int col = 6; col <= 11; col++) // F–K
            Assert.All(
                ws.Column(col).CellsUsed().Where(c => c.Address.RowNumber > 9),
                cell => Assert.True(string.IsNullOrEmpty(cell.GetString())));
    }

    [Fact]
    public void Export_GrandTotalAmount_MatchesDtoExactly()
    {
        WfpReportDto report = SampleReport();
        byte[] result = _sut.Export(report);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        IXLWorksheet ws = wb.Worksheets.First();

        IXLCell grandTotalLabelCell = ws.CellsUsed()
            .First(c => c.GetString() == "GRAND-TOTAL");
        decimal writtenTotal = ws.Cell(grandTotalLabelCell.Address.RowNumber, 15).GetValue<decimal>(); // col O
        Assert.Equal(report.FundSourceReports[0].Breakdown.GrandTotal.TotalAppropriation, writtenTotal);
    }

    [Fact]
    public void Export_NoFundSources_StillReturnsValidWorkbook()
    {
        WfpReportDto empty = new(2027, "PPDO", "Test Office", 0.10m, Array.Empty<WfpReportFundSourceDto>());
        byte[] result = _sut.Export(empty);
        using IXLWorkbook wb = new XLWorkbook(new MemoryStream(result));
        Assert.Single(wb.Worksheets);
    }
}
