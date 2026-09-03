using ClosedXML.Excel;
using PPDO.Application.Services;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// Unit tests for <see cref="AipXlsmParser"/> (RAL-64).
/// Builds minimal in-memory XLWorkbook instances and asserts the parsed hierarchy.
/// Covers: level detection by segment count, multi-line continuation, sheet filtering.
///
/// <para>
/// ⚠️ <b>Units.</b> The province's workbook is denominated in ₱000; storage is PESOS since V18-35
/// (PPDO-34). Every amount asserted here is therefore the cell value ×1000 — a cell holding
/// <c>500000.0</c> parses to ₱500,000,000. That is a conversion at the import edge, not a
/// survivor of the ×1000 the migration deleted from the ceiling path.
/// </para>
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
        Assert.Equal(500000000m,            act.Ps);
        Assert.Equal(200000000m,            act.Mooe);
        Assert.Equal(700000000m,            act.Total);
    }

    // ── Source units: the workbook is ₱000, storage is pesos (V18-35 / PPDO-34) ───

    [Fact]
    public void Parse_AmountsAreConvertedFromWorkbookThousandsToPesos()
    {
        // The one assertion that would catch the import edge losing its conversion. Without it
        // an upload writes thousands into a peso column and silently divides the record by 1000
        // — the same failure the unit migration exists to remove, relocated to the import path.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value  = "A-B-C-D-1-1-1-1";
            ws.Cell(14, 5).Value  = "Activity";
            ws.Cell(14, 12).Value = 250.0;   // PS, as the province writes it: ₱250 thousand
            ws.Cell(14, 16).Value = 10.0;    // CC Adaptation, likewise
        });

        ParsedAipActivity act = _sut.Parse(s)["GENERAL"][0].Programs[0].Projects[0].Activities[0];

        // ₱250,000. Not ₱250 — and the computed Total follows its components.
        Assert.Equal(250_000m, act.Ps);
        Assert.Equal(250_000m, act.Total);
        Assert.Equal(10_000m,  act.CcAdaptation);
    }

    [Fact]
    public void Parse_BlankAmountCell_StaysNull_RatherThanBecomingZero()
    {
        // NULL × 1000 must stay NULL: an uncosted activity has no amount, and a 0 would read as
        // "costed at nothing" everywhere downstream, including the dashboard's costed counts.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1-1-1-1";
            ws.Cell(14, 5).Value = "Uncosted activity";
        });

        ParsedAipActivity act = _sut.Parse(s)["GENERAL"][0].Programs[0].Projects[0].Activities[0];

        Assert.Null(act.Ps);
        Assert.Null(act.CcAdaptation);
        Assert.Null(act.Total);
    }

    // ── Program/project-level line items (RAL-108) ─────────────────────────────

    [Fact]
    public void Parse_ProgramLevelRow_WithLineItemData_CapturesLineItem()
    {
        // A program row that carries its own amounts/eSRE/funding source with no child project
        // (e.g. the Provincial Legal Office's "1000-000-1-01-011-004").
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
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProgram prog = result["GENERAL"][0].Programs[0];
        Assert.Empty(prog.Projects);
        Assert.NotNull(prog.LineItem);
        Assert.Equal("1000-000-1-01-011-004", prog.LineItem!.RefCode);
        Assert.Equal("ID",       prog.LineItem.EsreCode);
        Assert.Equal("PLO",      prog.LineItem.ImplementingOffice);
        Assert.Equal("January",  prog.LineItem.StartDate);
        Assert.Equal("December", prog.LineItem.EndDate);
        Assert.Equal("Human rights protected", prog.LineItem.ExpectedOutputs);
        Assert.Equal("GF",       prog.LineItem.FundingSourceRaw);
        Assert.Equal(50000000m,     prog.LineItem.Ps);
        // Computed (RAL-144 convention), same as a real activity row — never trusted from a
        // source Total column.
        Assert.Equal(50000000m,     prog.LineItem.Total);
    }

    [Fact]
    public void Parse_ProjectLevelRow_WithLineItemData_CapturesLineItem()
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
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProject proj = result["SOCIAL"][0].Programs[0].Projects[0];
        Assert.Empty(proj.Activities);
        Assert.NotNull(proj.LineItem);
        Assert.Equal("A-B-C-D-1-1-1", proj.LineItem!.RefCode);
        Assert.Equal("GAD",   proj.LineItem.FundingSourceRaw);
        Assert.Equal(25000000m,  proj.LineItem.Mooe);
        Assert.Equal(25000000m,  proj.LineItem.Total);
    }

    [Fact]
    public void Parse_ProgramLevelRow_WithoutLineItemData_LineItemIsNull()
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

        Assert.Null(result["GENERAL"][0].Programs[0].LineItem);
    }

    [Fact]
    public void Parse_ProjectLevelRow_WithoutLineItemData_LineItemIsNull()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("SOCIAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-1-1";
            ws.Cell(16, 4).Value = "Project";
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        Assert.Null(result["SOCIAL"][0].Programs[0].Projects[0].LineItem);
    }

    // ── Total is computed, never trusted from the source column (RAL-144) ──────

    [Fact]
    public void Parse_BlankSourceTotal_ComputesFromPsMooeCo()
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
            ws.Cell(17, 1).Value = "A-B-C-D-1-1-1-1";
            ws.Cell(17, 5).Value = "Hiring of additional manpower";
            // col 12 (Ps) and col 14 (Co) left blank; col 15 (Total) also left blank.
            ws.Cell(17, 13).Value = 2320.50;  // MOOE only
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipActivity act = result["OTHERS"][0].Programs[0].Projects[0].Activities[0];
        Assert.Null(act.Ps);
        Assert.Equal(2320500m, act.Mooe);
        Assert.Null(act.Co);
        Assert.Equal(2320500m, act.Total);
    }

    [Fact]
    public void Parse_ZeroSourceTotal_IgnoresStaleZero_ComputesFromComponents()
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
            ws.Cell(17, 1).Value = "A-B-C-D-1-1-1-1";
            ws.Cell(17, 5).Value = "Conduct of research studies";
            ws.Cell(17, 13).Value = 1875.00;  // MOOE
            ws.Cell(17, 15).Value = 0.0;      // Total — stale/literal zero in source
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipActivity act = result["OTHERS"][0].Programs[0].Projects[0].Activities[0];
        Assert.Equal(1875000m, act.Total);
    }

    [Fact]
    public void Parse_NoBudgetComponentsAtAll_TotalStaysNull()
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
            ws.Cell(17, 1).Value = "A-B-C-D-1-1-1-1";
            ws.Cell(17, 5).Value = "Activity with no budget line item";
            // Ps/Mooe/Co/Total all left blank.
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipActivity act = result["OTHERS"][0].Programs[0].Projects[0].Activities[0];
        Assert.Null(act.Ps);
        Assert.Null(act.Mooe);
        Assert.Null(act.Co);
        Assert.Null(act.Total);
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

    // ── Level detection from the description column (RAL-238) ─────────────────
    //
    // The province's real FY2027 AIP does not encode ref-code depth consistently: 82 of 2,887
    // rows have a segment count that disagrees with the description column they are indented
    // into. The description column (B/C/D/E) is authoritative; the segment count is a fallback.

    [Fact]
    public void Parse_7SegmentCode_ButTextInActivityColumn_CreatesActivity()
    {
        // SOCIAL_FY2027 r26: "3000-000-1-01-003-001-001" (7 segments) with its text in col E.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("SOCIAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-1-1";
            ws.Cell(16, 4).Value = "Project";
            ws.Cell(17, 1).Value  = "A-B-C-D-1-1-2";        // 7 segments — code says Project
            ws.Cell(17, 5).Value  = "Actually an activity";  // but text is in col E
            ws.Cell(17, 13).Value = 1836.614;
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProject proj = result["SOCIAL"][0].Programs[0].Projects[0];
        Assert.Single(proj.Activities);
        Assert.Equal("Actually an activity", proj.Activities[0].Name);
        Assert.Equal(1836614m, proj.Activities[0].Mooe);
        // Must NOT have become a second project.
        Assert.Single(result["SOCIAL"][0].Programs[0].Projects);
    }

    [Fact]
    public void Parse_9SegmentCode_WithTextInActivityColumn_CreatesActivity_NotDropped()
    {
        // ECONOMIC_FY2027 r897: "8000-000-1-01-016-004-001-003-001" (9 segments) —
        // fell outside the 5..8 switch and was silently discarded.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("ECONOMIC_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-1-1";
            ws.Cell(16, 4).Value = "Project";
            ws.Cell(17, 1).Value  = "A-B-C-D-1-1-1-1-1";   // 9 segments
            ws.Cell(17, 5).Value  = "Operation of Seed Production Farm";
            ws.Cell(17, 13).Value = 2032.945;
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProject proj = result["ECONOMIC"][0].Programs[0].Projects[0];
        Assert.Single(proj.Activities);
        Assert.Equal("Operation of Seed Production Farm", proj.Activities[0].Name);
        Assert.Equal(2032945m, proj.Activities[0].Mooe);
    }

    [Fact]
    public void Parse_5SegmentCode_ButTextInProgramColumn_CreatesProgram_NotNamelessOffice()
    {
        // SOCIAL_FY2027 r24/r27/r30/r35/r40/r56: office-depth codes whose text sits in col C.
        // The old parser created a ParsedAipOffice with an empty Name and orphaned everything
        // that followed onto it.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("SOCIAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office of the Governor - Housing";
            ws.Cell(15, 1).Value = "A-B-C-D-2";                 // 5 segments — code says Office
            ws.Cell(15, 3).Value = "Sustainable Housing Program"; // but text is in col C
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        Assert.Single(result["SOCIAL"]);
        ParsedAipOffice off = result["SOCIAL"][0];
        Assert.Equal("Office of the Governor - Housing", off.Name);
        Assert.Single(off.Programs);
        Assert.Equal("Sustainable Housing Program", off.Programs[0].Name);
        Assert.DoesNotContain(result["SOCIAL"], o => string.IsNullOrWhiteSpace(o.Name));
    }

    [Fact]
    public void Parse_BlankRefCode_WithProjectColumnText_CreatesProject_AndChildrenAttach()
    {
        // OTHERS_FY2027 r228: a project row with a description in col D but an empty col A.
        // The old parser treated blank col A as an activity-name continuation, found no
        // lastActivity, skipped the row, and then dropped rows 229-233 for having no project.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("OTHERS_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Environment and Natural Resources Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-2";
            ws.Cell(15, 3).Value = "Sustainable Mineral Program";
            // r16: no ref code, description in col D → a Project
            ws.Cell(16, 4).Value  = "Mining Compliance, Monitoring, and Support";
            ws.Cell(17, 1).Value  = "A-B-C-D-1-2-1-1";
            ws.Cell(17, 5).Value  = "Monitoring and supervision of CSAG, ISAG";
            ws.Cell(17, 13).Value = 1531.25;
            ws.Cell(18, 1).Value  = "A-B-C-D-1-2-1-2";
            ws.Cell(18, 5).Value  = "Manpower augmentation through hiring";
            ws.Cell(18, 13).Value = 10642.50;
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProgram prog = result["OTHERS"][0].Programs[0];
        Assert.Single(prog.Projects);
        ParsedAipProject proj = prog.Projects[0];
        Assert.Equal("Mining Compliance, Monitoring, and Support", proj.Name);
        Assert.False(string.IsNullOrWhiteSpace(proj.RefCode));   // synthesized, but present
        Assert.Equal(2, proj.Activities.Count);
        Assert.Equal(1531250m,  proj.Activities[0].Mooe);
        Assert.Equal(10642500m, proj.Activities[1].Mooe);
    }

    [Fact]
    public void Parse_ActivityWithNoPrecedingProject_IsNotSilentlyDropped()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("OTHERS_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            // No project row at all — activity arrives orphaned.
            ws.Cell(16, 1).Value  = "A-B-C-D-1-1-9-1";
            ws.Cell(16, 5).Value  = "Orphaned activity carrying real money";
            ws.Cell(16, 13).Value = 12892.50;
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProgram prog = result["OTHERS"][0].Programs[0];
        Assert.Single(prog.Projects);
        Assert.Single(prog.Projects[0].Activities);
        Assert.Equal(12892500m, prog.Projects[0].Activities[0].Mooe);
    }

    [Fact]
    public void Parse_ContinuationShapedRow_ThatCarriesAmounts_BecomesItsOwnActivity()
    {
        // GENERAL_FY2027 r188: col A holds the literal text "None", the description is in col E,
        // and the row carries ₱8,850,000. Treating it as a name continuation folded the text
        // into the previous activity and discarded the money. Four such rows exist in the real
        // file, worth ₱29,350,000 in total.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Sangguniang Panlalawigan Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-1-1";
            ws.Cell(16, 4).Value = "Project";
            ws.Cell(17, 1).Value  = "A-B-C-D-1-1-1-1";
            ws.Cell(17, 5).Value  = "Enactment of ordinances";
            ws.Cell(17, 13).Value = 130129.93;
            ws.Cell(18, 1).Value  = "None";                                    // literal "None"
            ws.Cell(18, 5).Value  = "Creation and filing up complementary legislative posts";
            ws.Cell(18, 13).Value = 8850.00;                                   // carries money
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProject proj = result["GENERAL"][0].Programs[0].Projects[0];
        Assert.Equal(2, proj.Activities.Count);
        Assert.Equal("Enactment of ordinances", proj.Activities[0].Name);
        Assert.Equal(130129930m, proj.Activities[0].Mooe);
        Assert.Equal("Creation and filing up complementary legislative posts", proj.Activities[1].Name);
        Assert.Equal(8850000m, proj.Activities[1].Mooe);
        Assert.False(string.IsNullOrWhiteSpace(proj.Activities[1].RefCode));
    }

    [Fact]
    public void Parse_ContinuationRow_WithNoAmounts_StillAppendsToName()
    {
        // The money-carrying case above must not break ordinary multi-line wrapping.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Office";
            ws.Cell(15, 1).Value = "A-B-C-D-1-1";
            ws.Cell(15, 3).Value = "Program";
            ws.Cell(16, 1).Value = "A-B-C-D-1-1-1";
            ws.Cell(16, 4).Value = "Project";
            ws.Cell(17, 1).Value  = "A-B-C-D-1-1-1-1";
            ws.Cell(17, 5).Value  = "First part";
            ws.Cell(17, 13).Value = 500.0;
            ws.Cell(18, 1).Value  = "None";
            ws.Cell(18, 5).Value  = "second part";   // no amounts → continuation
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipProject proj = result["GENERAL"][0].Programs[0].Projects[0];
        ParsedAipActivity act = Assert.Single(proj.Activities);
        Assert.Equal("First part second part", act.Name);
        Assert.Equal(500000m, act.Mooe);
    }

    [Fact]
    public void Parse_HeaderRowWithDescriptionText_IsNotTreatedAsAnOffice()
    {
        // Row 8 of every real sheet holds "AIP Reference Code (1)" in col A and
        // "Program/Project/Activity Description (2)" in col B (merged B8:E9). Neither is data.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(8, 1).Value  = "AIP Reference Code           \n(1)";
            ws.Cell(8, 2).Value  = "Program/Project/Activity Description                (2)";
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Real Office";
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        Assert.Single(result["GENERAL"]);
        Assert.Equal("Real Office", result["GENERAL"][0].Name);
    }

    [Fact]
    public void Parse_TotalRow_IsStillSkipped()
    {
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value = "A-B-C-D-1";
            ws.Cell(14, 2).Value = "Real Office";
            ws.Cell(20, 1).Value  = "TOTAL";
            ws.Cell(20, 12).Value = 8798.65;
            ws.Cell(21, 1).Value  = "GRAND TOTAL";
            ws.Cell(21, 12).Value = 17777422.68;
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        Assert.Single(result["GENERAL"]);
        Assert.Equal("Real Office", result["GENERAL"][0].Name);
    }

    [Fact]
    public void Parse_CorrectlyCodedFile_ParsesIdentically_Regression()
    {
        // A file where the segment count and the description column agree must be unaffected.
        using Stream s = BuildStream(wb =>
        {
            IXLWorksheet ws = wb.Worksheets.Add("GENERAL_FY2027");
            ws.Cell(14, 1).Value  = "1000-000-1-01-010";
            ws.Cell(14, 2).Value  = "Provincial Planning and Development Office";
            ws.Cell(15, 1).Value  = "1000-000-1-01-010-001";
            ws.Cell(15, 3).Value  = "Planning Program";
            ws.Cell(16, 1).Value  = "1000-000-1-01-010-001-001";
            ws.Cell(16, 4).Value  = "Planning Project";
            ws.Cell(17, 1).Value  = "1000-000-1-01-010-001-001-001";
            ws.Cell(17, 5).Value  = "Planning Activity";
            ws.Cell(17, 12).Value = 100.5;
            ws.Cell(17, 13).Value = 200.25;
            ws.Cell(17, 14).Value = 300.0;
        });

        Dictionary<string, List<ParsedAipOffice>> result = _sut.Parse(s);

        ParsedAipOffice off = result["GENERAL"][0];
        Assert.Equal("Provincial Planning and Development Office", off.Name);
        ParsedAipProgram prog = Assert.Single(off.Programs);
        Assert.Equal("Planning Program", prog.Name);
        ParsedAipProject proj = Assert.Single(prog.Projects);
        Assert.Equal("Planning Project", proj.Name);
        ParsedAipActivity act = Assert.Single(proj.Activities);
        Assert.Equal("Planning Activity", act.Name);
        Assert.Equal(100500m,  act.Ps);
        Assert.Equal(200250m, act.Mooe);
        Assert.Equal(300000m,  act.Co);
        Assert.Equal(600750m, act.Total);
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
