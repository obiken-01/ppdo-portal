using ClosedXML.Excel;
using PPDO.Application.Services;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="AipXlsmParser"/> (RAL-64).
/// Builds minimal in-memory XLWorkbook instances and asserts the parsed hierarchy.
/// Covers: level detection by segment count, multi-line continuation, sheet filtering.
/// </summary>
public sealed class AipXlsmParserTests
{
    private readonly AipXlsmParser _sut = new();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Stream BuildStream(Action<XLWorkbook> configure)
    {
        using XLWorkbook wb = new();
        configure(wb);
        MemoryStream ms = new();
        wb.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    // ── Level detection ───────────────────────────────────────────────────────

    [Fact]
    public void Parse_5SegmentCode_CreatesOfficeNode()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";   // 5 segments → Office
            ws.Cell(14, 2).Value = "Main Office";
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        Assert.True(result.ContainsKey("GENERAL"));
        Assert.Single(result["GENERAL"]);
        ParsedAipOffice off = result["GENERAL"][0];
        Assert.Equal("A-B-C-D-1",   off.RefCode);
        Assert.Equal("Main Office", off.Name);
        Assert.Equal("GENERAL",     off.Sector);
    }

    [Fact]
    public void Parse_6SegmentCode_CreatesProgram_UnderCorrectOffice()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("SOCIAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";      // Office
            ws.Cell(14, 2).Value = "Social Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";    // Program
            ws.Cell(15, 3).Value = "Social Program";
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        Assert.Single(result["SOCIAL"]);
        ParsedAipOffice off = result["SOCIAL"][0];
        Assert.Single(off.Programs);
        Assert.Equal("A-B-C-D-1-1",    off.Programs[0].RefCode);
        Assert.Equal("Social Program",  off.Programs[0].Name);
    }

    [Fact]
    public void Parse_7SegmentCode_CreatesProject_UnderCorrectProgram()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("ECONOMIC_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Eco Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-2";
            ws.Cell(15, 3).Value = "Eco Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-2-3";    // Project
            ws.Cell(16, 4).Value = "Eco Project";
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProgram prog = result["ECONOMIC"][0].Programs[0];
        Assert.Single(prog.Projects);
        Assert.Equal("A-B-C-D-1-2-3", prog.Projects[0].RefCode);
        Assert.Equal("Eco Project",    prog.Projects[0].Name);
    }

    [Fact]
    public void Parse_8SegmentCode_CreatesActivity_WithAmountFields()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("OTHERS_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-1-1";
            ws.Cell(16, 4).Value = "Project";
            ws.Cell(17, 1).Value  = "A-B-C-D-1-1-1-1";  // Activity
            ws.Cell(17, 5).Value  = "Build something";
            ws.Cell(17, 6).Value  = "SS";               // EsreCode
            ws.Cell(17, 11).Value = "GF";               // FundingSourceRaw
            ws.Cell(17, 12).Value = 500000.0;           // PS
            ws.Cell(17, 13).Value = 200000.0;           // MOOE
            ws.Cell(17, 15).Value = 700000.0;           // Total
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipActivity act = result["OTHERS"][0].Programs[0].Projects[0].Activities[0];
        Assert.Equal("A-B-C-D-1-1-1-1", act.RefCode);
        Assert.Equal("Build something",  act.Name);
        Assert.Equal("SS",               act.EsreCode);
        Assert.Equal("GF",               act.FundingSourceRaw);
        Assert.Equal(500000m,            act.Ps);
        Assert.Equal(200000m,            act.Mooe);
        Assert.Equal(700000m,            act.Total);
    }

    // ── Program/project-level line items (RAL-108, extend approach) ─────────

    [Fact]
    public void Parse_ProgramLevelRow_WithLineItemData_CapturesFieldsOnProgramItself()
    {
        // Provincial Legal Office fixture from the ticket: a program row that
        // carries its own amounts/eSRE/funding source with no child project.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value  = "1000-000-1-01-011";
            ws.Cell(14, 2).Value  = "Provincial Legal Office";
            ws.Cell(15, 1).Value  = "1000-000-1-01-011-004";       // Program level (6 segments)
            ws.Cell(15, 3).Value  = "DISASTER RESILIENT HUMAN RIGHTS AND JUSTICE PROGRAM";
            ws.Cell(15, 6).Value  = "ID";                          // EsreCode
            ws.Cell(15, 7).Value  = "PLO";                         // ImplementingOffice
            ws.Cell(15, 8).Value  = "January";                     // StartDate
            ws.Cell(15, 9).Value  = "December";                    // EndDate
            ws.Cell(15, 10).Value = "Human rights protected";      // ExpectedOutputs
            ws.Cell(15, 11).Value = "GF";                          // FundingSourceRaw
            ws.Cell(15, 12).Value = 50000.0;                       // PS
            ws.Cell(15, 15).Value = 50000.0;                       // Total
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProgram prog = result["GENERAL"][0].Programs[0];
        Assert.Empty(prog.Projects);
        Assert.Equal("ID",       prog.EsreCode);
        Assert.Equal("PLO",      prog.ImplementingOffice);
        Assert.Equal("January",  prog.StartDate);
        Assert.Equal("December", prog.EndDate);
        Assert.Equal("Human rights protected", prog.ExpectedOutputs);
        Assert.Equal("GF",       prog.FundingSourceRaw);
        Assert.Equal(50000m,     prog.Ps);
        Assert.Equal(50000m,     prog.Total);
    }

    [Fact]
    public void Parse_ProjectLevelRow_WithLineItemData_CapturesFieldsOnProjectItself()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("SOCIAL_FY2027");
            ws.Cell(14, 1).Value  = "A-B-C-D-1";
            ws.Cell(14, 2).Value  = "Office";
            ws.Cell(15, 1).Value  = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value  = "Program";
            ws.Cell(16, 1).Value  = "A-B-C-D-1-1-1";     // Project level (7 segments)
            ws.Cell(16, 4).Value  = "Project with its own line item";
            ws.Cell(16, 11).Value = "GAD";                // FundingSourceRaw
            ws.Cell(16, 13).Value = 25000.0;              // MOOE
            ws.Cell(16, 15).Value = 25000.0;              // Total
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProject proj = result["SOCIAL"][0].Programs[0].Projects[0];
        Assert.Empty(proj.Activities);
        Assert.Equal("GAD",   proj.FundingSourceRaw);
        Assert.Equal(25000m,  proj.Mooe);
        Assert.Equal(25000m,  proj.Total);
    }

    [Fact]
    public void Parse_ProgramLevelRow_WithoutLineItemData_FieldsAreNull()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProgram prog = result["GENERAL"][0].Programs[0];
        Assert.Null(prog.EsreCode);
        Assert.Null(prog.Ps);
        Assert.Null(prog.Total);
        Assert.Null(prog.FundingSourceRaw);
    }

    [Fact]
    public void Parse_BlankRefCode_AfterActivity_AppendsColEToName()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-1-1";
            ws.Cell(16, 4).Value = "Project";
            ws.Cell(17, 1).Value = "A-B-C-D-1-1-1-1";
            ws.Cell(17, 5).Value = "First part";
            ws.Cell(18, 5).Value = "second part";      // col A blank → continuation
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipActivity act = result["GENERAL"][0].Programs[0].Projects[0].Activities[0];
        Assert.Equal("First part second part", act.Name);
    }

    [Fact]
    public void Parse_UnrecognisedSheets_AreIgnored()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("BUDGET_FY2027");   // not a known prefix
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Should not appear";

            // One real sheet so no AipParseException
            IXLWorksheet ws2 = wb.Worksheets.Add("GENERAL_FY2027");
            // (empty — zero data rows)
        });

        // Should not throw; BUDGET_ sheet produces zero offices
        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);
        Assert.False(result.ContainsKey("BUDGET"));
        Assert.True(result.ContainsKey("GENERAL"));
        Assert.Empty(result["GENERAL"]);
    }

    [Fact]
    public void Parse_NoRecognisedSheets_ThrowsAipParseException()
    {
        using Stream s = BuildStream(wb =>
        {
            wb.Worksheets.Add("SUMMARY");  // no recognised prefix
        });

        Assert.Throws<AipParseException>(() => _sut.Parse(s));
    }

    [Fact]
    public void Parse_AllFourSectorSheets_ReturnsDistinctSectorKeys()
    {
        using Stream s = BuildStream(wb =>
        {
            foreach (string name in new[] { "GENERAL_FY2027", "SOCIAL_FY2027", "ECONOMIC_FY2027", "OTHERS_FY2027" })
                wb.Worksheets.Add(name);
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        Assert.True(result.ContainsKey("GENERAL"));
        Assert.True(result.ContainsKey("SOCIAL"));
        Assert.True(result.ContainsKey("ECONOMIC"));
        Assert.True(result.ContainsKey("OTHERS"));
    }
}
