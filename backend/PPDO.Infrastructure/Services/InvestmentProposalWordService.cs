using System.Globalization;
using System.Reflection;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal.Document;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// The investment proposal as a .docx (v1.8.0 Demo 2.15 — PPDO-158, Investment_Proposal_Spec.md
/// decision 27). Opens the embedded template, which carries the letterhead header, the
/// "Page X of Y" footer, A4 with 1.78 cm margins and Verdana 10 pt, and writes the body.
///
/// <para>
/// Section titles, column headings and fixed labels are the template's, word for word (decision
/// 30). No guidance text is printed (decision 21). Everything else, the order, the numbering, the
/// totals and the merged cells, arrives decided in the <see cref="ProposalDocument"/>.
/// </para>
///
/// <para>
/// Lists are written as text ("•", "1.") with a hanging indent rather than Word numbering: a
/// numbering definition per list is a lot of XML for no difference on paper, and plain text can
/// never restart or continue a number by accident.
/// </para>
/// </summary>
public sealed class InvestmentProposalWordService : IInvestmentProposalWordService
{
    public const string TemplateResource = "PPDO.Infrastructure.Templates.InvestmentProposalTemplate.docx";

    /// <summary>The A4 width less the template's 1009-twip margins.</summary>
    private const int ContentWidth = 11906 - 2 * 1009;

    private const string HeaderShade = "D9D9D9";
    private const string SmallSize   = "16";   // 8 pt, the H-1 money cells (samples use 8–9 pt)
    private const string TitleSize   = "28";   // 14 pt

    public byte[] Export(ProposalDocument d)
    {
        using MemoryStream stream = new();
        using (Stream template = Assembly.GetExecutingAssembly().GetManifestResourceStream(TemplateResource)
                                 ?? throw new InvalidOperationException($"Embedded template {TemplateResource} is missing."))
            template.CopyTo(stream);

        using (WordprocessingDocument doc = WordprocessingDocument.Open(stream, true))
        {
            Body body = doc.MainDocumentPart!.Document.Body!;
            SectionProperties section = body.Elements<SectionProperties>().Last();
            foreach (OpenXmlElement el in body.ChildElements.Where(e => e is not SectionProperties).ToList()) el.Remove();

            foreach (OpenXmlElement el in Content(d)) body.InsertBefore(el, section);

            doc.PackageProperties.Title   = d.SectionA.ProjectTitle;
            doc.PackageProperties.Subject = d.Title;
            doc.MainDocumentPart.Document.Save();
        }
        return stream.ToArray();
    }

    // ── The body, in template order ───────────────────────────────────────────

    private static IEnumerable<OpenXmlElement> Content(ProposalDocument d)
    {
        yield return P(d.Title, bold: true, size: TitleSize, align: JustificationValues.Center, after: 240);

        // A
        yield return Heading("A. PROJECT SUMMARY");
        yield return SectionA(d.SectionA);
        yield return P("Disaggregated Data of Intended Beneficiaries", bold: true, before: 160, after: 80);
        yield return Beneficiaries("Indicator", [d.SectionA.Beneficiaries.Rows], [null], d.SectionA.Beneficiaries.Total);

        // B, C
        yield return Heading("B. PROJECT DESCRIPTION");
        foreach (OpenXmlElement p in Rich(d.Description, after: 120)) yield return p;
        yield return Heading("C. RATIONALE/BACKGROUND");
        foreach (OpenXmlElement p in Rich(d.Rationale, after: 120)) yield return p;

        // D
        yield return Heading("D. PROJECT BENEFITS AND COSTS");
        yield return Grid([2600, 3644, 3644],
            [HeaderRow(["SECTOR", "PROJECT BENEFIT", "PROJECT COST"], [2600, 3644, 3644])],
            d.Benefits.Select(b => Row(
                Cell(2600, P(b.Sector)),
                Cell(3644, Rich(b.Benefit)),
                Cell(3644, Rich(b.Cost)))));

        // E
        yield return Heading("E. LOGICAL FRAMEWORK");
        // The goals are their own table, apart from the project structure (Ralph, 2026-10-03, after the
        // samples). Two tables with nothing between them merge in Word, hence the spacer.
        yield return Grid([2600, 7288], [],
            [Row(Cell(2600, [P("General Goals/Objectives", bold: true)], shade: HeaderShade), Cell(7288, Rich(d.GeneralObjective)))]);
        yield return Spacer();
        yield return Grid([2600, 3644, 3644],
            [HeaderRow(["Project Structure", "Performance Target and/or Indicator", "Means of Verification"], [2600, 3644, 3644])],
            d.Logframe.Select(l => Row(Cell(2600, P(l.Label, bold: true)), Cell(3644, P(l.Target)), Cell(3644, P(l.Verification)))));

        // F
        yield return Heading("F. TARGET BENEFICIARIES");
        yield return Beneficiaries("Target Beneficiaries",
            [d.TargetBeneficiaries.Direct, d.TargetBeneficiaries.Indirect],
            ["Direct Beneficiaries", "Indirect Beneficiaries"],
            d.TargetBeneficiaries.Total);

        // G
        yield return Heading("G. IMPLEMENTATION SCHEDULE /WORK PLAN");
        int[] g = [2600, 2200, 2200, 1600, 1288];
        yield return Grid(g,
            [HeaderRow(["Inputs/ Activities/Project Components", "Performance Target and/or Indicator",
                        "Gender Issues to be addressed", "Timeline/\nDuration", "OPR"], g)],
            d.WorkPlan.Select(l => l.IsGroupHeader
                ? Row(Cell(ContentWidth, [P(l.Text, bold: true)], span: 5))
                : Row(Cell(g[0], P($"{l.Number}. {l.Text}")), Cell(g[1], P(l.PerformanceTarget)),
                      Cell(g[2], P(l.GenderIssues)), Cell(g[3], P(l.Timeline)), Cell(g[4], P(l.Opr)))));

        // H
        yield return Heading("H. ESTIMATED COST/BUDGETARY REQUIREMENTS (Annex H-1)");
        yield return AnnexH1(d.AnnexH1);

        // I
        yield return Heading("I. IMPLEMENTING TEAM");
        yield return Grid([3300, 6588], [],
            [
                Row(Cell(3300, P("Overall Project Supervisor:", bold: true)), Cell(6588, P(d.Team.ProjectSupervisor))),
                Row(Cell(3300, P("Project Manager", bold: true)), Cell(6588, P(d.Team.ProjectManager))),
                Row(Cell(3300, P(null)), Cell(6588, P(null))),   // the template's blank third row
            ]);
        yield return Spacer();
        int[] team = [3300, 900, 3044, 2644];
        yield return Grid(team,
            [HeaderRow(["Members of the Implementation Team", "Sex", "GAD-related Trainings Attended", "Expertise"], team)],
            OrEmpty(d.Team.Members.Select(m => Row(Cell(team[0], P(m.Name)), Cell(team[1], P(m.Sex, align: JustificationValues.Center)),
                Cell(team[2], P(m.GadTrainings)), Cell(team[3], P(m.Expertise)))), team));
        yield return P("Required Capacity Development Training of the Implementation Team", bold: true, before: 160, after: 80);
        yield return Grid([3300, 6588],
            [HeaderRow(["Members of the Implementation Team", "Required Capacity Development Training"], [3300, 6588])],
            OrEmpty(d.Team.Trainings.Select(t => Row(
                Cell(3300, P(t.Member)),
                // Decision 31: the first row of a run restarts the merge; the rows it covers continue it.
                Cell(6588, [P(t.RowSpan == 0 ? null : t.Training)],
                    vMerge: t.RowSpan > 1 ? MergedCellValues.Restart : t.RowSpan == 0 ? MergedCellValues.Continue : null))),
                [3300, 6588]));

        // J
        yield return Heading("J. PARTNERSHIP AND SUSTAINABILITY");
        foreach (OpenXmlElement p in Rich(d.PartnershipSustainability, after: 120)) yield return p;

        // K
        yield return Heading("K. MONITORING AND EVALUATION");
        int[] k = [3800, 2600, 3488];
        yield return Grid(k,
            [HeaderRow(["M&E Activity/Scheme/Mechanism", "Schedule/Frequency", "Monitoring Tools to be Used"], k)],
            d.Monitoring.SelectMany(phase =>
                new[] { Row(Cell(ContentWidth, [P(phase.Label, bold: true)], span: 3)) }
                    .Concat(phase.Rows.Select(r => Row(Cell(k[0], P(r.Activity)), Cell(k[1], P(r.Schedule)), Cell(k[2], P(r.Tools)))))));

        // L
        yield return Heading("L. RISK MANAGEMENT");
        int[] l3 = [3296, 3296, 3296];
        yield return Grid(l3,
            [HeaderRow(["Possible Risks", "Preventive measures and strategies", "Mechanisms to Monitor Risks"], l3)],
            OrEmpty(d.Risks.Select(r => Row(Cell(l3[0], P(r.Risk)), Cell(l3[1], P(r.Prevention)), Cell(l3[2], P(r.Monitoring)))), l3)
                .Append(Row(Cell(ContentWidth,
                    [P("Strategies to avoid/minimize negative impact on women’s status and welfare", bold: true), P(d.WomensImpactStrategy)],
                    span: 3))));

        // M
        yield return Para(Spacing(before: 240, after: 240),
            R("M. CLIMATE CHANGE EXPENDITURE TYPOLOGY: ", bold: true), R(d.ClimateTypology));

        // Signatures
        if (d.Signatories.Count > 0) yield return Signatures(d.Signatories);
    }

    // ── Section A ─────────────────────────────────────────────────────────────

    private static Table SectionA(DocSectionA a)
    {
        int[] w = [2700, 2394, 2400, 2394];
        TableRow Single(string label, string? value)
            => Row(Cell(w[0], P(label, bold: true)), Cell(w[1] + w[2] + w[3], [P(value)], span: 3));

        return Grid(w, [],
            [
                Single("Program Title", a.ProgramTitle),   // PPDC's addition (decision 6)
                Single("Project Title", a.ProjectTitle),
                Single("Project Proponent", a.Proponent),
                Single("Project Type", a.ProjectType),
                Single("Project Location", a.ProjectLocation),
                Row(Cell(w[0], P("Implementation Schedule", bold: true)),
                    Cell(w[1], Para(null, R("Start: ", italic: true), R(a.ScheduleStart))),
                    Cell(w[2] + w[3], [Para(null, R("End: ", italic: true), R(a.ScheduleEnd))], span: 2)),
                Row(Cell(w[0], P("Project Cost", bold: true)), Cell(w[1], P(Money(a.ProjectCost))),
                    Cell(w[2], P("Attributed GAD Budget", bold: true)), Cell(w[3], P(Money(a.AttributedGadBudget)))),
                Single("Funding Source", a.FundingSource),
                Row(Cell(w[0], P("HGDG Checklist Used", bold: true)), Cell(w[1], P(a.HgdgChecklist)),
                    Cell(w[2], P("HGDG Score", bold: true)),
                    Cell(w[3], P(a.HgdgScore?.ToString("0.0", CultureInfo.InvariantCulture)))),
            ]);
    }

    /// <summary>A beneficiary table: optional group-label rows, the rows, then TOTAL.</summary>
    private static Table Beneficiaries(
        string firstHeading, IReadOnlyList<IReadOnlyList<DocBeneficiaryRow>> groups, IReadOnlyList<string?> groupLabels,
        DocBeneficiaryRow total)
    {
        int[] w = [5388, 1500, 1500, 1500];
        TableRow Line(DocBeneficiaryRow r, bool bold = false)
            => Row(Cell(w[0], P(r.Label, bold: bold)), Cell(w[1], P(Count(r.Male), bold: bold, align: JustificationValues.Center)),
                Cell(w[2], P(Count(r.Female), bold: bold, align: JustificationValues.Center)),
                Cell(w[3], P(Count(r.Total), bold: bold, align: JustificationValues.Center)));

        List<TableRow> rows = [];
        for (int i = 0; i < groups.Count; i++)
        {
            if (groupLabels[i] is string label) rows.Add(Row(Cell(ContentWidth, [P(label, bold: true)], span: 4)));
            rows.AddRange(groups[i].Select(r => Line(r)));
        }
        rows.Add(Line(total, bold: true));
        return Grid(w, [HeaderRow([firstHeading, "Male", "Female", "Total"], w)], rows);
    }

    // ── Annex H-1 ─────────────────────────────────────────────────────────────

    private static Table AnnexH1(DocAnnexH1 h)
    {
        // Input | MOOE | PS | CO | Total | Source of Fund (decision 17).
        int[] w = [2100, 2400, 1400, 1500, 1188, 1300];
        TableCell Head(int width, string text, int span = 1, MergedCellValues? vMerge = null)
            => Cell(width, [P(text, bold: true, align: JustificationValues.Center)], span: span, shade: HeaderShade, vMerge: vMerge);

        List<TableRow> header =
        [
            Row(true,
                Head(w[0], "Input/Activities/Project Components", vMerge: MergedCellValues.Restart),
                Head(w[1] + w[2] + w[3], "Budgetary Requirements and Other Inputs", span: 3),
                Head(w[4], "Total", vMerge: MergedCellValues.Restart),
                Head(w[5], "Source of Fund", vMerge: MergedCellValues.Restart)),
            Row(true,
                Cell(w[0], [P(null)], shade: HeaderShade, vMerge: MergedCellValues.Continue),
                Head(w[1], "MOOE"), Head(w[2], "PS"), Head(w[3], "CO"),
                Cell(w[4], [P(null)], shade: HeaderShade, vMerge: MergedCellValues.Continue),
                Cell(w[5], [P(null)], shade: HeaderShade, vMerge: MergedCellValues.Continue)),
        ];

        List<TableRow> rows = h.Lines.Select(l => l.IsGroupHeader
            ? Row(Cell(ContentWidth, [P(l.Text, bold: true)], span: 6))
            : Row(
                Cell(w[0], P(l.Text, size: SmallSize)),
                Cell(w[1], MoneyCell(l.Mooe)), Cell(w[2], MoneyCell(l.Ps)), Cell(w[3], MoneyCell(l.Co)),
                Cell(w[4], P(Money(l.Total), size: SmallSize, align: JustificationValues.Right)),
                Cell(w[5], P(l.SourceOfFund, size: SmallSize)))).ToList();

        TableCell Sum(int width, decimal value)
            => Cell(width, P(Money(value), bold: true, size: SmallSize, align: JustificationValues.Right));
        rows.Add(Row(Cell(w[0], P(InvestmentProposalDocumentBuilder.TotalRow, bold: true)),
            Sum(w[1], h.MooeTotal), Sum(w[2], h.PsTotal), Sum(w[3], h.CoTotal), Sum(w[4], h.GrandTotal), Cell(w[5], P(null))));

        return Grid(w, header, rows);
    }

    /// <summary>A money cell: each expenditure block, then the class total right-aligned.</summary>
    private static IEnumerable<OpenXmlElement> MoneyCell(DocAnnexCell? cell)
    {
        if (cell is null) yield break;
        foreach (DocAnnexBlock b in cell.Blocks)
        {
            if (b.Amount is decimal amount)
            {
                yield return P($"{b.Heading} {Money(amount)}", size: SmallSize);
                continue;
            }
            yield return P(b.Heading, size: SmallSize);
            foreach (string line in b.Lines) yield return P(line, size: SmallSize, indent: 120);
        }
        yield return cell.Blocks.Count == 0
            ? P(Money(cell.Total), size: SmallSize, align: JustificationValues.Right)
            : P($"Total {Money(cell.Total)}", bold: true, size: SmallSize, align: JustificationValues.Right, before: 60);
    }

    // ── Signatures: two per row, no borders (decision 19) ─────────────────────
    // Every block is centred in its column, and an odd last block is centred across the page, as in the
    // template and the samples (Ralph, 2026-10-03).

    private static Table Signatures(IReadOnlyList<DocSignatory> slots)
    {
        int half = ContentWidth / 2;
        IEnumerable<OpenXmlElement> Block(DocSignatory s)
        {
            string label = s.Label.Length == 0 || s.Label.EndsWith(':') ? s.Label : s.Label + ":";
            yield return P(label, align: JustificationValues.Center);
            yield return P(null);
            yield return P(null);
            yield return P(s.Name, bold: true, align: JustificationValues.Center);
            yield return P(s.Position, align: JustificationValues.Center);
        }

        List<TableRow> rows = [];
        for (int i = 0; i < slots.Count; i += 2)
        {
            if (i + 1 == slots.Count)
            {
                rows.Add(Row(Cell(ContentWidth, Block(slots[i]), span: 2)));
                break;
            }
            rows.Add(Row(Cell(half, Block(slots[i])), Cell(half, Block(slots[i + 1]))));
            if (i + 2 < slots.Count) rows.Add(Row(Cell(half, P(null)), Cell(half, P(null))));
        }

        Table t = Grid([half, half], [], rows);
        t.GetFirstChild<TableProperties>()!.TableBorders = new TableBorders(
            new TopBorder { Val = BorderValues.None }, new LeftBorder { Val = BorderValues.None },
            new BottomBorder { Val = BorderValues.None }, new RightBorder { Val = BorderValues.None },
            new InsideHorizontalBorder { Val = BorderValues.None }, new InsideVerticalBorder { Val = BorderValues.None });
        return t;
    }

    // ── Rich text ─────────────────────────────────────────────────────────────

    private static IEnumerable<OpenXmlElement> Rich(RichTextContent content, int after = 0)
    {
        if (content.IsEmpty)
        {
            yield return P(null);
            yield break;
        }
        foreach (RichBlock block in content.Blocks)
        {
            List<OpenXmlElement> runs = [];
            if (block.Kind != RichBlockKind.Paragraph)
                runs.Add(R(block.Kind == RichBlockKind.Numbered ? $"{block.Number}.\t" : "•\t"));
            foreach (RichRun run in block.Runs)
                runs.Add(run.IsBreak ? new Run(new Break()) : R(run.Text, bold: run.Bold, italic: run.Italic));

            ParagraphProperties props = Spacing(after: after);
            if (block.Kind != RichBlockKind.Paragraph)
            {
                int left = 360 * (block.Level + 1);
                props.Indentation = new Indentation { Left = left.ToString(), Hanging = "360" };
                props.SpacingBetweenLines = new SpacingBetweenLines { After = "0" };
            }
            yield return Para(props, runs.ToArray());
        }
    }

    // ── Primitives ────────────────────────────────────────────────────────────

    private static Paragraph Heading(string text)
        => P(text, bold: true, before: 240, after: 120, keepNext: true);

    private static Paragraph Spacer() => P(null, after: 80);

    private static ParagraphProperties Spacing(int before = 0, int after = 0)
        => new() { SpacingBetweenLines = new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() } };

    private static Paragraph Para(ParagraphProperties? props, params OpenXmlElement[] runs)
    {
        Paragraph p = new();
        if (props is not null) p.AppendChild(props);
        foreach (OpenXmlElement r in runs) p.AppendChild(r);
        return p;
    }

    /// <summary>
    /// One paragraph. A null or empty text is an empty paragraph (a cell must hold one). Line
    /// breaks in plain multi-line fields become <c>&lt;w:br/&gt;</c>.
    /// </summary>
    private static Paragraph P(
        string? text, bool bold = false, bool italic = false, string? size = null,
        JustificationValues? align = null, int before = 0, int after = 0, int indent = 0, bool keepNext = false)
    {
        ParagraphProperties props = Spacing(before, after);
        if (align is JustificationValues jc) props.Justification = new Justification { Val = jc };
        if (indent > 0) props.Indentation = new Indentation { Left = indent.ToString() };
        if (keepNext) props.KeepNext = new KeepNext();

        Paragraph p = new(props);
        if (string.IsNullOrEmpty(text)) return p;

        string[] lines = text.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) p.AppendChild(new Run(new Break()));
            if (lines[i].Length > 0) p.AppendChild(R(lines[i], bold, italic, size));
        }
        return p;
    }

    private static Run R(string? text, bool bold = false, bool italic = false, string? size = null)
    {
        RunProperties props = new();
        if (bold) props.Bold = new Bold();
        if (italic) props.Italic = new Italic();
        if (size is not null)
        {
            props.FontSize = new FontSize { Val = size };
            props.FontSizeComplexScript = new FontSizeComplexScript { Val = size };
        }
        // Tabs inside text are real tabs in Word; everything else goes in as text.
        Run run = new(props);
        string[] parts = XmlSafe(text ?? string.Empty).Split('\t');
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) run.AppendChild(new TabChar());
            if (parts[i].Length > 0) run.AppendChild(new Text(parts[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return run;
    }

    /// <summary>
    /// Drops the characters XML 1.0 cannot hold. A pasted control character would otherwise make
    /// Word refuse the file ("unreadable content"), which is the one failure no validator catches
    /// after the fact.
    /// </summary>
    private static string XmlSafe(string text)
    {
        if (text.All(XmlConvertIsValid)) return text;
        StringBuilder sb = new(text.Length);
        foreach (char c in text) if (XmlConvertIsValid(c)) sb.Append(c);
        return sb.ToString();
    }

    private static bool XmlConvertIsValid(char c)
        => c is '\t' or '\n' or '\r' || (c >= 0x20 && c <= 0xD7FF) || char.IsSurrogate(c) || (c >= 0xE000 && c <= 0xFFFD);

    private static string Money(decimal? amount) => amount is decimal a ? InvestmentProposalDocumentBuilder.Money(a) : string.Empty;

    private static string? Count(int? value) => value?.ToString("#,0", CultureInfo.InvariantCulture);

    private static TableRow Row(params TableCell[] cells) => Row(false, cells);

    private static TableRow Row(bool repeatAsHeader, params TableCell[] cells)
    {
        TableRow row = new();
        TableRowProperties props = new(new CantSplit());
        if (repeatAsHeader) props.AppendChild(new TableHeader());
        row.AppendChild(props);
        foreach (TableCell c in cells) row.AppendChild(c);
        return row;
    }

    private static TableRow HeaderRow(IReadOnlyList<string> headings, IReadOnlyList<int> widths)
        => Row(true, headings.Select((h, i) =>
            Cell(widths[i], [P(h, bold: true, align: JustificationValues.Center)], shade: HeaderShade)).ToArray());

    private static TableCell Cell(int width, Paragraph paragraph) => Cell(width, [paragraph]);

    private static TableCell Cell(
        int width, IEnumerable<OpenXmlElement> content, int span = 1, string? shade = null, MergedCellValues? vMerge = null)
    {
        TableCellProperties props = new(new TableCellWidth { Width = width.ToString(), Type = TableWidthUnitValues.Dxa });
        if (span > 1) props.GridSpan = new GridSpan { Val = span };
        if (vMerge is MergedCellValues merge) props.VerticalMerge = new VerticalMerge { Val = merge };
        if (shade is not null) props.Shading = new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = shade };

        TableCell cell = new(props);
        List<OpenXmlElement> body = content.ToList();
        // A cell must end with a paragraph, or Word asks to repair the file.
        if (body.Count == 0 || body[^1] is not Paragraph) body.Add(new Paragraph());
        foreach (OpenXmlElement el in body) cell.AppendChild(el);
        return cell;
    }

    /// <summary>A repeating table with no rows still prints one empty row, as the template does.</summary>
    private static IEnumerable<TableRow> OrEmpty(IEnumerable<TableRow> rows, IReadOnlyList<int> widths)
    {
        List<TableRow> list = rows.ToList();
        return list.Count > 0 ? list : [Row(widths.Select(w => Cell(w, P(null))).ToArray())];
    }

    private static Table Grid(IReadOnlyList<int> widths, IEnumerable<TableRow> header, IEnumerable<TableRow> rows)
    {
        Table t = new(
            new TableProperties(
                new TableStyle { Val = "TableGrid" },
                new TableWidth { Width = ContentWidth.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 }),
                new TableLayout { Type = TableLayoutValues.Fixed }),
            new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString() })));
        foreach (TableRow r in header) t.AppendChild(r);
        foreach (TableRow r in rows) t.AppendChild(r);
        return t;
    }
}
