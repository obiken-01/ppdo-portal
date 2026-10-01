using System.IO.Compression;
using System.Reflection;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.DTOs.InvestmentProposal.Document;
using PPDO.Infrastructure.Services;

namespace PPDO.Tests.Infrastructure;

/// <summary>
/// The investment proposal's Word writer (PPDO-158, Investment_Proposal_Spec.md decision 27 and
/// §11). Every test reads the saved file back with the Open XML SDK.
///
/// <para>
/// ⚠️ <b>Zero <see cref="OpenXmlValidator"/> errors is the merge gate.</b> A schema slip (a cell
/// without a paragraph, an element out of order) is what makes Word open a file with "We found
/// unreadable content… repair?", and it is invisible to every other assertion here.
/// </para>
/// </summary>
public sealed class InvestmentProposalWordServiceTests
{
    private readonly InvestmentProposalWordService _sut = new();

    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static ProposalItemDto Item(string name, decimal price, decimal qty, string unit, decimal days)
        => new(name, price, qty, unit, days, price * qty * days);

    private static ProposalAipRowDto Activity(int id, string name, params ProposalExpenditureDto[] lines)
        => new(id, $"1000-000-1-{id:00}", name, "January–December 2028", "PPDO",
            lines.Sum(l => l.Ps), lines.Sum(l => l.Mooe), lines.Sum(l => l.Co), lines.Sum(l => l.Total),
            lines.Select(l => l.FundName).OfType<string>().Distinct().ToList(), lines);

    private static ProposalExpenditureDto Line(string title, decimal ps = 0m, decimal mooe = 0m, decimal co = 0m,
        IReadOnlyList<ProposalItemDto>? items = null)
        => new("5-02-01-010", title, ps, mooe, co, ps + mooe + co, "General Fund", items ?? []);

    private static ProposalContentDto EmptyContent() => new(
        ProjectLocation: null, HgdgChecklist: null, HgdgScore: null,
        BeneficiariesSummary: [], Description: null, Rationale: null,
        Benefits: InvestmentProposalSector.All.Select(s => new ProposalBenefitDto(s, null, null)).ToList(),
        GeneralObjective: null,
        Logframe: InvestmentProposalLogframeLevel.All.Select(l => new ProposalLogframeDto(l, null, null)).ToList(),
        DirectSameAsSummary: true, TargetBeneficiaries: [], Groups: [], WorkPlan: [],
        ProjectSupervisor: null, ProjectManager: null, TeamMembers: [], PartnershipSustainability: null,
        Monitoring: [], Risks: [], WomensImpactStrategy: null,
        Signatories: [new(1, "Prepared by", null, null), new(2, "Submitted by", null, null), new(3, "Noted by", null, null), new(4, null, null, null)]);

    private static ProposalDto Dto(IReadOnlyList<ProposalAipRowDto> rows, ProposalContentDto content) => new(
        1, InvestmentProposalStatus.Draft, "AAAAAAAAB9E=", null, null, new DateTime(2026, 10, 1), null,
        false, true, false,
        new ProposalHeaderDto(10, 20, 30, 15, 2028, "Development Planning Program", "1000-000-1",
            "Formulation of Plans", "PPDO", "January 2028", "December 2028",
            rows.Sum(r => r.Total), ["General Fund"], "N/A", 3_055_100m),
        new ProposalWarningsDto([]), content, rows);

    /// <summary>Every section filled, every merge and list kind used.</summary>
    private static ProposalDocument Full()
    {
        ProposalAipRowDto[] rows =
        [
            Activity(1, "Mangrove Restoration",
                Line("Meals", mooe: 33_200m, items: [Item("Breakfast", 295m, 30m, "pax", 2m), Item("Snacks AM", 155m, 50m, "pax", 2m)]),
                Line("Planting Stocks and Other Materials", mooe: 25_000m)),
            Activity(2, "Monitoring", Line("Honoraria", ps: 12_345.67m), Line("Drone", co: 200_000m)),
            new(3, "1000-000-1-03", "Synthetic", "March 2028", "PPDO", 0m, 999.99m, 0m, 999.99m, ["General Fund"], []),
        ];
        ProposalContentDto content = EmptyContent() with
        {
            ProjectLocation = "San Jose, Occidental Mindoro",
            HgdgChecklist = HgdgChecklists.All[0].Code,
            HgdgScore = 8m,
            BeneficiariesSummary = [new(null, "Farmers", 3, 5), new(null, "Label only", null, null)],
            Description = "<p>A <strong>bold</strong> and <em>italic</em> start.</p><ul><li>One<ul><li>Nested</li></ul></li></ul>",
            Rationale = "<ol><li>First</li><li>Second</li></ol><p>Line<br>break</p>",
            Benefits = InvestmentProposalSector.All.Select(s => new ProposalBenefitDto(s, $"<p>{s} benefit</p>", null)).ToList(),
            GeneralObjective = "<p>Objective</p>",
            Logframe = InvestmentProposalLogframeLevel.All.Select(l => new ProposalLogframeDto(l, $"{l} target\nsecond line", "Reports")).ToList(),
            DirectSameAsSummary = true,
            TargetBeneficiaries = [new(null, InvestmentProposalBeneficiarySection.Indirect, "Families", 10, 12)],
            Groups = [new(1, "g1", "Capability Building")],
            WorkPlan =
            [
                new(null, 1, null, "g1", "100 ha", "Women's participation", null, null),
                new(null, 2, null, null, null, null, null, null),
                new(null, 3, null, null, null, null, null, null),
                new(null, null, "Liquidation", null, "Liquidated", null, "December 2028", "Accounting"),
            ],
            ProjectSupervisor = "PPDC", ProjectManager = "Planning Officer IV",
            TeamMembers =
            [
                new(null, "Ana", "F", "GST", "Planning", "Gender Sensitivity Training"),
                new(null, "Ben", "M", null, "GIS", "Gender Sensitivity Training"),
                new(null, "Carlo", "M", null, null, null),
            ],
            PartnershipSustainability = "<p>Partners</p>",
            Monitoring = [new(null, InvestmentProposalMonitoringPhase.During, "Site visits", "Monthly", "Checklist")],
            Risks = [new(null, "Typhoon", "Reschedule", "PAGASA bulletins")],
            WomensImpactStrategy = "Safe spaces",
            Signatories = [new(1, "Prepared by", "Ana Cruz", "Planning Officer IV"), new(2, "Submitted by", "Ben Reyes", "PPDC"),
                           new(3, "Noted by", "Gov. Name", "Local Chief Executive"), new(4, null, null, null)],
        };
        return InvestmentProposalDocumentBuilder.Build(Dto(rows, content));
    }

    private static ProposalDocument Empty() => InvestmentProposalDocumentBuilder.Build(Dto([], EmptyContent()));

    private static WordprocessingDocument Open(byte[] bytes) => WordprocessingDocument.Open(new MemoryStream(bytes), false);

    private static string BodyText(byte[] bytes)
    {
        using WordprocessingDocument doc = Open(bytes);
        return string.Join("\n", doc.MainDocumentPart!.Document.Body!.Descendants<Paragraph>().Select(p => p.InnerText));
    }

    private static List<string> Errors(byte[] bytes)
    {
        using WordprocessingDocument doc = Open(bytes);
        return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc)
            .Select(e => $"{e.Path?.XPath}: {e.Description}").ToList();
    }

    /// <summary>The H-1 table: the one whose header names "Source of Fund".</summary>
    private static Table AnnexTable(WordprocessingDocument doc)
        => doc.MainDocumentPart!.Document.Body!.Elements<Table>().Single(t => t.InnerText.Contains("Source of Fund"));

    // ── Validity ──────────────────────────────────────────────────────────────

    [Fact]
    public void Export_FullProposal_OpensAndValidates()
        => Assert.Empty(Errors(_sut.Export(Full())));

    [Fact]
    public void Export_EmptyProposal_OpensAndValidates()
        => Assert.Empty(Errors(_sut.Export(Empty())));

    [Fact]
    public void Export_ControlCharactersInTypedText_AreDropped_AndTheFileStaysValid()
    {
        ProposalDocument d = Full();
        d = d with { SectionA = d.SectionA with { ProjectLocation = "San\u0001 Jose\u000B" } };
        byte[] bytes = _sut.Export(d);

        Assert.Empty(Errors(bytes));
        Assert.Contains("San Jose", BodyText(bytes));
    }

    [Fact]
    public void Export_EveryTableCellEndsWithAParagraph()
    {
        using WordprocessingDocument doc = Open(_sut.Export(Full()));
        Assert.All(doc.MainDocumentPart!.Document.Body!.Descendants<TableCell>(), c => Assert.IsType<Paragraph>(c.LastChild));
    }

    [Fact]
    public void Export_EveryTable_GridMatchesItsRows_AndFillsThePageWidth()
    {
        // OpenXmlValidator accepts a grid that disagrees with the rows; Word then lays the table out
        // on its own guess. Each row's spans must add up to the grid, and the grid to the text width.
        using WordprocessingDocument doc = Open(_sut.Export(Full()));
        foreach (Table t in doc.MainDocumentPart!.Document.Body!.Elements<Table>())
        {
            List<GridColumn> grid = t.GetFirstChild<TableGrid>()!.Elements<GridColumn>().ToList();
            Assert.Equal(11906 - 2 * 1009, grid.Sum(g => int.Parse(g.Width!.Value!)));
            foreach (TableRow r in t.Elements<TableRow>())
                Assert.Equal(grid.Count, r.Elements<TableCell>().Sum(c => c.TableCellProperties?.GridSpan?.Val?.Value ?? 1));
        }
    }

    // ── Template: letterhead, footer, page setup ──────────────────────────────

    [Fact]
    public void Export_KeepsTheTemplateLetterheadFooterAndPageSetup()
    {
        using WordprocessingDocument doc = Open(_sut.Export(Full()));
        MainDocumentPart main = doc.MainDocumentPart!;

        Assert.Contains("PROVINCIAL GOVERNMENT OF OCCIDENTAL MINDORO", main.HeaderParts.Single().Header.InnerText);
        Assert.Equal(2, main.HeaderParts.Single().ImageParts.Count());
        string footer = main.FooterParts.Single().Footer.OuterXml;
        Assert.Contains("PAGE", footer);
        Assert.Contains("NUMPAGES", footer);

        SectionProperties section = main.Document.Body!.Elements<SectionProperties>().Single();
        Assert.Equal(11906u, section.GetFirstChild<PageSize>()!.Width!.Value);   // A4
        Assert.Equal(1009, section.GetFirstChild<PageMargin>()!.Top!.Value);

        RunFonts fonts = main.StyleDefinitionsPart!.Styles!.DocDefaults!.RunPropertiesDefault!.RunPropertiesBaseStyle!.RunFonts!;
        Assert.Equal("Verdana", fonts.Ascii!.Value);
    }

    [Fact]
    public void Template_IsSmall_AndCarriesNoAuthorNames()
    {
        using Stream s = typeof(InvestmentProposalWordService).Assembly
            .GetManifestResourceStream(InvestmentProposalWordService.TemplateResource)!;
        Assert.True(s.Length < 400_000, $"template is {s.Length} bytes");

        using ZipArchive zip = new(s);
        Assert.True(zip.GetEntry("word/media/image1.png")!.Length < 200_000);
        using StreamReader core = new(zip.GetEntry("docProps/core.xml")!.Open());
        Assert.DoesNotContain("Ecamina", core.ReadToEnd());
        Assert.Null(zip.GetEntry("docProps/custom.xml"));
    }

    // ── Content ───────────────────────────────────────────────────────────────

    [Fact]
    public void Export_TitleAndEverySectionHeading_InTemplateOrder()
    {
        string text = BodyText(_sut.Export(Full()));
        string[] headings =
        [
            "PGOM INVESTMENT PROPOSAL", "A. PROJECT SUMMARY", "Disaggregated Data of Intended Beneficiaries",
            "B. PROJECT DESCRIPTION", "C. RATIONALE/BACKGROUND", "D. PROJECT BENEFITS AND COSTS",
            "E. LOGICAL FRAMEWORK", "F. TARGET BENEFICIARIES", "G. IMPLEMENTATION SCHEDULE /WORK PLAN",
            "H. ESTIMATED COST/BUDGETARY REQUIREMENTS (Annex H-1)", "I. IMPLEMENTING TEAM",
            "Required Capacity Development Training of the Implementation Team",
            "J. PARTNERSHIP AND SUSTAINABILITY", "K. MONITORING AND EVALUATION", "L. RISK MANAGEMENT",
            "M. CLIMATE CHANGE EXPENDITURE TYPOLOGY: N/A",
        ];
        int at = -1;
        foreach (string h in headings)
        {
            int next = text.IndexOf(h, StringComparison.Ordinal);
            Assert.True(next > at, $"'{h}' missing or out of order");
            at = next;
        }
        Assert.DoesNotContain("PGOM PROJECT PROPOSAL", text);
    }

    [Fact]
    public void Export_NoTemplateGuidanceText()
    {
        string text = BodyText(_sut.Export(Full()));
        Assert.DoesNotContain("Provide a brief description", text);
        Assert.DoesNotContain("Benefits refer to", text);
        Assert.DoesNotContain("Impact refers to", text);
    }

    [Fact]
    public void Export_SectionA_ProgramTitleAboveProjectTitle_CostAndGadBudget()
    {
        string text = BodyText(_sut.Export(Full()));
        Assert.True(text.IndexOf("Program Title", StringComparison.Ordinal) < text.IndexOf("Project Title", StringComparison.Ordinal));
        Assert.Contains("3,055,100.00", text);
        Assert.Contains(HgdgChecklists.All[0].Name, text);
        Assert.Contains("8.0", text);
    }

    [Fact]
    public void Export_WorkPlan_NumberedStraightThrough()
    {
        string text = BodyText(_sut.Export(Full()));
        Assert.Contains("1. Mangrove Restoration", text);
        Assert.Contains("2. Monitoring", text);
        Assert.Contains("4. Liquidation", text);
    }

    [Fact]
    public void Export_AnnexH1_OneRowPerLine_PlusTwoHeaderRowsAndTotal()
    {
        ProposalDocument d = Full();
        using WordprocessingDocument doc = Open(_sut.Export(d));
        Table h1 = AnnexTable(doc);

        Assert.Equal(2 + d.AnnexH1.Lines.Count + 1, h1.Elements<TableRow>().Count());
        Assert.Equal(["MOOE", "PS", "CO"],
            h1.Elements<TableRow>().ElementAt(1).Elements<TableCell>().Skip(1).Take(3).Select(c => c.InnerText));
        Assert.Contains("Breakfast  295.00 x 30 pax x 2 days = 17,700.00", h1.InnerText);
        Assert.Contains("Planting Stocks and Other Materials: 25,000.00", h1.InnerText);
        Assert.Contains(InvestmentProposalDocumentBuilder.Money(d.AnnexH1.GrandTotal), h1.Elements<TableRow>().Last().InnerText);
    }

    [Fact]
    public void Export_Trainings_IdenticalNeighboursMergeVertically()
    {
        using WordprocessingDocument doc = Open(_sut.Export(Full()));
        Table trainings = doc.MainDocumentPart!.Document.Body!.Elements<Table>()
            .Single(t => t.InnerText.Contains("Required Capacity Development Training"));
        List<TableCell> column = trainings.Elements<TableRow>().Skip(1).Select(r => r.Elements<TableCell>().Last()).ToList();

        Assert.Equal(MergedCellValues.Restart, column[0].TableCellProperties!.VerticalMerge!.Val!.Value);
        Assert.Equal(MergedCellValues.Continue, column[1].TableCellProperties!.VerticalMerge!.Val!.Value);
        Assert.Null(column[2].TableCellProperties!.VerticalMerge);
        Assert.Equal("", column[1].InnerText);   // a continued cell carries no text of its own
    }

    [Fact]
    public void Export_Signatures_OnlyNamedSlots_TwoPerRow()
    {
        using WordprocessingDocument doc = Open(_sut.Export(Full()));
        Table sig = doc.MainDocumentPart!.Document.Body!.Elements<Table>().Last();

        Assert.Contains("Prepared by:", sig.InnerText);
        Assert.Contains("Noted by:", sig.InnerText);
        Assert.Contains("Local Chief Executive", sig.InnerText);
        // 3 named slots: [1 | 2], spacer, [3 | empty]
        Assert.Equal(3, sig.Elements<TableRow>().Count());
        Assert.Equal(2, sig.Elements<TableRow>().First().Elements<TableCell>().Count());
    }

    [Fact]
    public void Export_NoNamedSignatory_PrintsNoSignatureTable()
    {
        using WordprocessingDocument doc = Open(_sut.Export(Empty()));
        Assert.DoesNotContain(doc.MainDocumentPart!.Document.Body!.Elements<Table>(), t => t.InnerText.Contains("Prepared by"));
    }

    [Fact]
    public void Export_RichText_ListsPrintTheirMarkers_AndFormattingSurvives()
    {
        byte[] bytes = _sut.Export(Full());
        string text = BodyText(bytes);
        Assert.Contains("1.", text);
        Assert.Contains("•", text);

        using WordprocessingDocument doc = Open(bytes);
        Run bold = doc.MainDocumentPart!.Document.Body!.Descendants<Run>().First(r => r.InnerText == "bold");
        Assert.NotNull(bold.RunProperties?.Bold);
    }
}
