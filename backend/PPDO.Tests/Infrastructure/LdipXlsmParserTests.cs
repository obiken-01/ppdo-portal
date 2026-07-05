using ClosedXML.Excel;
using PPDO.Application.Services;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="LdipXlsmParser"/> (RAL-113).
/// Builds minimal in-memory XLWorkbook instances and asserts the parsed hierarchy.
/// Covers: level detection by which description column is populated (col B = office,
/// col C = program — NOT ref-code segment count, since the real file is inconsistent
/// about program ref-code depth), sheet filtering (only the 4 sector sheets), and the
/// full detail-column set.
/// </summary>
public sealed class LdipXlsmParserTests
{
    private readonly LdipXlsmParser _sut = new();

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
    public void Parse_OfficeNameInColumnB_CreatesOfficeNode()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("General");
            ws.Cell(10, 1).Value = "A-B-C-D-1";
            ws.Cell(10, 2).Value = "Main Office";
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);

        Assert.True(result.ContainsKey("GENERAL"));
        Assert.Single(result["GENERAL"]);
        ParsedLdipOffice off = result["GENERAL"][0];
        Assert.Equal("A-B-C-D-1",   off.RefCode);
        Assert.Equal("Main Office", off.Name);
        Assert.Equal("GENERAL",     off.Sector);
        Assert.Empty(off.Programs);
    }

    [Fact]
    public void Parse_ProgramNameInColumnC_CreatesProgram_UnderCorrectOffice()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("Social");
            ws.Cell(10, 1).Value = "A-B-C-D-1";      // Office
            ws.Cell(10, 2).Value = "Social Office";
            ws.Cell(11, 1).Value = "A-B-C-D-1-1";    // Program, 6-segment code (the common case)
            ws.Cell(11, 3).Value = "Social Program";
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);

        Assert.Single(result["SOCIAL"]);
        ParsedLdipOffice off = result["SOCIAL"][0];
        Assert.Single(off.Programs);
        Assert.Equal("A-B-C-D-1-1",    off.Programs[0].RefCode);
        Assert.Equal("Social Program", off.Programs[0].Name);
    }

    [Fact]
    public void Parse_ProgramRow_SameRefCodeDepthAsOffice_StillDetectedByColumnC()
    {
        // Regression test: a real section of the reference LDIP workbook (Social
        // sheet) gives programs a ref code at the SAME segment depth as their
        // office row, incrementing only the last segment, instead of appending an
        // extra "-NNN" segment like the rest of the file. A segment-count-based
        // rule misclassifies these as blank-named offices and silently drops their
        // budget/detail — 6 real programs disappeared this way (199 vs the correct
        // 205 when checked against the actual file). Column-content detection
        // (col C populated = program) handles this correctly regardless of depth.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("Social");
            ws.Cell(13, 1).Value = "3000-000-1-01-001";   // Office (5 segments)
            ws.Cell(13, 2).Value = "OFFICE OF THE GOVERNOR - AKAP-HUB";
            ws.Cell(14, 1).Value = "3000-000-1-01-003";   // Program — ALSO 5 segments
            ws.Cell(14, 3).Value = "AKAP HUB Health Assistance Program";
            ws.Cell(14, 12).Value = 20400.0;              // MOOE
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);

        ParsedLdipOffice off = Assert.Single(result["SOCIAL"]);
        Assert.Equal("OFFICE OF THE GOVERNOR - AKAP-HUB", off.Name);
        ParsedLdipProgram prog = Assert.Single(off.Programs);
        Assert.Equal("AKAP HUB Health Assistance Program", prog.Name);
        Assert.Equal(20400m, prog.Mooe);
    }

    [Fact]
    public void Parse_ProgramRow_ReadsFullDetailColumnSet()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("Economic");
            ws.Cell(10, 1).Value = "A-B-C-D-1";
            ws.Cell(10, 2).Value = "Eco Office";
            ws.Cell(11, 1).Value  = "A-B-C-D-1-1";
            ws.Cell(11, 3).Value  = "Eco Program";
            ws.Cell(11, 6).Value  = "PEO";                 // Implementing office
            ws.Cell(11, 7).Value  = "2026";                 // Start date
            ws.Cell(11, 8).Value  = "2029";                 // Completion date
            ws.Cell(11, 9).Value  = "Roads built";          // Expected outputs
            ws.Cell(11, 10).Value = "General Fund";         // Funding source
            ws.Cell(11, 11).Value = 100000.0;               // PS
            ws.Cell(11, 12).Value = 200000.0;               // MOOE
            ws.Cell(11, 13).Value = 50000.0;                // CO
            ws.Cell(11, 14).Value = 350000.0;               // Total
            ws.Cell(11, 15).Value = 1000.0;                 // CC Adaptation
            ws.Cell(11, 16).Value = 2000.0;                 // CC Mitigation
            ws.Cell(11, 17).Value = "Typology A";           // CC Typology Code
            ws.Cell(11, 18).Value = "Chapter 14";           // PDP/RDP
            ws.Cell(11, 19).Value = "SDG 9";                // SDGs
            ws.Cell(11, 20).Value = "Priority 3";           // Sendai
            ws.Cell(11, 21).Value = "DRRM capacity";        // NDRRM Plan
            ws.Cell(11, 22).Value = "Public service";       // NSP
            ws.Cell(11, 23).Value = "PDPDFP tag";           // PDPDFP
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);
        ParsedLdipProgram prog = result["ECONOMIC"][0].Programs[0];

        Assert.Equal("PEO",           prog.ImplementingOffice);
        Assert.Equal("2026",          prog.StartDate);
        Assert.Equal("2029",          prog.EndDate);
        Assert.Equal("Roads built",   prog.ExpectedOutputs);
        Assert.Equal("General Fund",  prog.FundingSourceRaw);
        Assert.Equal(100000m,         prog.Ps);
        Assert.Equal(200000m,         prog.Mooe);
        Assert.Equal(50000m,          prog.Co);
        Assert.Equal(350000m,         prog.Total);
        Assert.Equal(1000m,           prog.CcAdaptation);
        Assert.Equal(2000m,           prog.CcMitigation);
        Assert.Equal("Typology A",    prog.CcTypologyCode);
        Assert.Equal("Chapter 14",    prog.PdpRdp);
        Assert.Equal("SDG 9",         prog.Sdgs);
        Assert.Equal("Priority 3",    prog.SendaiFramework);
        Assert.Equal("DRRM capacity", prog.NdrrmPlan);
        Assert.Equal("Public service",prog.Nsp);
        Assert.Equal("PDPDFP tag",    prog.Pdpdfp);
    }

    [Fact]
    public void Parse_MultipleProgramsUnderOneOffice_AllAttachedInOrder()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("Others");
            ws.Cell(10, 1).Value = "A-B-C-D-1";
            ws.Cell(10, 2).Value = "Office";
            ws.Cell(11, 1).Value = "A-B-C-D-1-1";
            ws.Cell(11, 3).Value = "Program One";
            ws.Cell(12, 1).Value = "A-B-C-D-1-2";
            ws.Cell(12, 3).Value = "Program Two";
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);
        List<ParsedLdipProgram> programs = result["OTHERS"][0].Programs;

        Assert.Equal(2, programs.Count);
        Assert.Equal("Program One", programs[0].Name);
        Assert.Equal("Program Two", programs[1].Name);
    }

    [Fact]
    public void Parse_ProgramRow_BeforeAnyOffice_IsSkipped()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("General");
            ws.Cell(10, 1).Value = "A-B-C-D-1-1";   // Program row with no preceding Office
            ws.Cell(10, 3).Value = "Orphan Program";
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);

        Assert.Empty(result["GENERAL"]);
    }

    [Fact]
    public void Parse_HeaderRowLabelText_IsNotMisreadAsAPhantomOffice()
    {
        // Regression test: the real workbook's header row has "AIP Reference Code
        // (1)" in column A and "Program/Project/Activity Description (2)" in
        // column B — no hyphen in column A, but column B is non-blank. Without a
        // hyphen guard on column A, this reads as a valid "office" row and
        // inflates the office count by one per sheet (31 real offices -> 35).
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("General");
            ws.Cell(8, 1).Value = "AIP Reference Code           \n(1)";
            ws.Cell(8, 2).Value = "Program/Project/Activity Description                                                             (2)";
            ws.Cell(10, 1).Value = "1000-000-1-01-010";
            ws.Cell(10, 2).Value = "Real Office";
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);

        Assert.Single(result["GENERAL"]);
        Assert.Equal("Real Office", result["GENERAL"][0].Name);
    }

    [Fact]
    public void Parse_TotalAndGrandTotalRows_AreSkipped()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("General");
            ws.Cell(10, 1).Value = "1000-000-1-01-010";
            ws.Cell(10, 2).Value = "Real Office";
            ws.Cell(11, 1).Value = "TOTAL";
            ws.Cell(12, 1).Value = "GRAND TOTAL";
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);

        Assert.Single(result["GENERAL"]);
        Assert.Empty(result["GENERAL"][0].Programs);
    }

    [Fact]
    public void Parse_UnrecognisedSheets_AreIgnored()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("Program Description Form"); // not a sector sheet
            ws.Cell(10, 1).Value = "A-B-C-D-1";
            ws.Cell(10, 2).Value = "Should not appear";

            // One real sheet so no LdipParseException
            IXLWorksheet ws2 = wb.Worksheets.Add("General");
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);
        Assert.False(result.ContainsKey("PROGRAM DESCRIPTION FORM"));
        Assert.True(result.ContainsKey("GENERAL"));
        Assert.Empty(result["GENERAL"]);
    }

    [Fact]
    public void Parse_AipAnnexSheet_IsIgnored()
    {
        using Stream s = BuildStream(wb =>
        {
            wb.Worksheets.Add("AIP 2027");   // the workbook's own AIP annex sheet — not read
            wb.Worksheets.Add("General");
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);
        Assert.False(result.ContainsKey("AIP 2027"));
        Assert.True(result.ContainsKey("GENERAL"));
    }

    [Fact]
    public void Parse_NoRecognisedSheets_ThrowsLdipParseException()
    {
        using Stream s = BuildStream(wb =>
        {
            wb.Worksheets.Add("SUMMARY");
        });

        Assert.Throws<LdipParseException>(() => _sut.Parse(s));
    }

    [Fact]
    public void Parse_AllFourSectorSheets_ReturnsDistinctSectorKeys()
    {
        using Stream s = BuildStream(wb =>
        {
            foreach (string name in new[] { "General", "Social", "Economic", "Others" })
                wb.Worksheets.Add(name);
        });

        Dictionary<string, List<ParsedLdipOffice>> result = _sut.Parse(s);

        Assert.True(result.ContainsKey("GENERAL"));
        Assert.True(result.ContainsKey("SOCIAL"));
        Assert.True(result.ContainsKey("ECONOMIC"));
        Assert.True(result.ContainsKey("OTHERS"));
    }
}
