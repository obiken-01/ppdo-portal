using PPDO.Application.Common;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.DTOs.InvestmentProposal.Document;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="InvestmentProposalDocumentBuilder"/> (PPDO-157): the proposal as it prints, built
/// from a <see cref="ProposalDto"/>. Covers every builder bullet of Investment_Proposal_Spec.md
/// §11: one grouping for G and H-1, ungrouped after grouped, empty groups skipped, proposal-only
/// rows blank in H-1, the computation line, expenditures without items, activities without lines,
/// the totals, Section M's "N/A", and F-Direct following Section A.
/// </summary>
public sealed class InvestmentProposalDocumentBuilderTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static ProposalItemDto Item(string name, decimal price, decimal qty, string unit, decimal days)
        => new(name, price, qty, unit, days, price * qty * days);

    private static ProposalExpenditureDto Line(
        string title, decimal ps = 0m, decimal mooe = 0m, decimal co = 0m,
        string? fund = "General Fund", params ProposalItemDto[] items)
        => new("5-02-01-010", title, ps, mooe, co, ps + mooe + co, fund, items);

    private static ProposalAipRowDto Activity(int id, string name, params ProposalExpenditureDto[] lines)
        => new(id, $"1000-000-1-{id:00}", name, "January–December 2028", "PPDO",
            lines.Sum(l => l.Ps), lines.Sum(l => l.Mooe), lines.Sum(l => l.Co), lines.Sum(l => l.Total),
            lines.Select(l => l.FundName).OfType<string>().Distinct().ToList(), lines);

    /// <summary>An activity with no expenditure lines, carrying its own amounts (decision 18).</summary>
    private static ProposalAipRowDto Bare(int id, string name, decimal ps, decimal mooe, decimal co)
        => new(id, $"1000-000-1-{id:00}", name, "March 2028", "PPDO", ps, mooe, co, ps + mooe + co, ["General Fund"], []);

    private static ProposalWorkPlanRowDto AipRow(int activityId, string? group = null, string? target = null)
        => new(null, activityId, null, group, target, null, null, null);

    private static ProposalWorkPlanRowDto Step(string name, string? group = null)
        => new(null, null, name, group, "Target", "Issue", "Quarterly", "PPDO");

    private static ProposalContentDto Content() => new(
        ProjectLocation: "San Jose", HgdgChecklist: null, HgdgScore: null,
        BeneficiariesSummary: [],
        Description: null, Rationale: null,
        Benefits: InvestmentProposalSector.All.Select(s => new ProposalBenefitDto(s, null, null)).ToList(),
        GeneralObjective: null,
        Logframe: InvestmentProposalLogframeLevel.All.Select(l => new ProposalLogframeDto(l, null, null)).ToList(),
        DirectSameAsSummary: true,
        TargetBeneficiaries: [],
        Groups: [], WorkPlan: [],
        ProjectSupervisor: null, ProjectManager: null,
        TeamMembers: [],
        PartnershipSustainability: null,
        Monitoring: [], Risks: [],
        WomensImpactStrategy: null,
        Signatories: [new(1, "Prepared by", null, null), new(2, "Submitted by", null, null), new(3, "Noted by", null, null), new(4, null, null, null)]);

    private static ProposalDto Proposal(
        IReadOnlyList<ProposalAipRowDto> rows, ProposalContentDto? content = null, string climate = "N/A")
        => new(
            Id: 1, Status: InvestmentProposalStatus.Draft, RowVersion: "AAAAAAAAB9E=",
            FinalizedAt: null, FinalizedByName: null, UpdatedAt: new DateTime(2026, 10, 1), UpdatedByName: null,
            AipChangedSinceFinal: false, CanEdit: true, CanReopen: false,
            Header: new ProposalHeaderDto(
                AipProjectId: 10, ProgramId: 20, AipOfficeId: 30, OfficeId: 15, FiscalYear: 2028,
                ProgramTitle: "Development Planning Program", ProjectRefCode: "1000-000-1",
                ProjectTitle: "Formulation of Plans", Proponent: "PPDO",
                ScheduleStart: "January 2028", ScheduleEnd: "December 2028",
                ProjectCost: rows.Sum(r => r.Total),
                FundingSources: rows.SelectMany(r => r.FundNames).Distinct().ToList(),
                ClimateTypology: climate, AttributedGadBudget: null),
            Warnings: new ProposalWarningsDto([]),
            Content: content ?? Content(),
            AipRows: rows);

    private static ProposalDocument Build(ProposalDto dto) => InvestmentProposalDocumentBuilder.Build(dto);

    // ── Title, file name, Section A ───────────────────────────────────────────

    [Fact]
    public void Title_IsThePpdcTitle()
        => Assert.Equal("PGOM INVESTMENT PROPOSAL", Build(Proposal([])).Title);

    [Fact]
    public void FileName_IsRefCodeAndName_WithUnsafeCharactersReplaced()
    {
        ProposalDto dto = Proposal([]);
        dto = dto with { Header = dto.Header with { ProjectTitle = "Roads: Phase 1/2?" } };
        Assert.Equal("Investment Proposal - 1000-000-1 - Roads_ Phase 1_2_.docx", Build(dto).FileName);
    }

    [Fact]
    public void SectionA_ProjectTypeIsBlank_AndChecklistPrintsItsName()
    {
        ProposalDto dto = Proposal([Activity(1, "A", Line("TEV", mooe: 100m))],
            Content() with { HgdgChecklist = HgdgChecklists.All[0].Code, HgdgScore = 8m });
        DocSectionA a = Build(dto).SectionA;

        Assert.Equal(string.Empty, a.ProjectType);
        Assert.Equal(HgdgChecklists.All[0].Name, a.HgdgChecklist);
        Assert.Equal("Development Planning Program", a.ProgramTitle);
        Assert.Equal("January 2028", a.ScheduleStart);
        Assert.Equal("December 2028", a.ScheduleEnd);
        Assert.Equal(100m, a.ProjectCost);
    }

    [Fact]
    public void SectionA_FundingSource_JoinsTheDistinctNames()
    {
        ProposalDto dto = Proposal([
            Activity(1, "A", Line("TEV", mooe: 100m, fund: "General Fund")),
            Activity(2, "B", Line("Drone", co: 200m, fund: "20% Development Fund")),
        ]);
        Assert.Equal("General Fund, 20% Development Fund", Build(dto).SectionA.FundingSource);
    }

    [Fact]
    public void Beneficiaries_RowTotalIsMalePlusFemale_AndTheTotalRowSums()
    {
        ProposalDto dto = Proposal([], Content() with
        {
            BeneficiariesSummary =
            [
                new(null, "Farmers", 3, 5),
                new(null, "Fisherfolk", 2, null),
                new(null, "Label only", null, null),
            ],
        });
        DocBeneficiaryTable t = Build(dto).SectionA.Beneficiaries;

        Assert.Equal([8, 2, null], t.Rows.Select(r => r.Total));
        Assert.Equal(new DocBeneficiaryRow("TOTAL", 5, 5, 10), t.Total);
    }

    [Fact]
    public void Beneficiaries_NoCountsAnywhere_TotalRowIsBlank()
        => Assert.Equal(new DocBeneficiaryRow("TOTAL", null, null, null), Build(Proposal([])).SectionA.Beneficiaries.Total);

    // ── F follows A while "Same as Section A" is on ───────────────────────────

    [Fact]
    public void TargetBeneficiaries_SameAsSummary_PrintsSectionARowsAsDirect()
    {
        ProposalDto dto = Proposal([], Content() with
        {
            BeneficiariesSummary = [new(null, "Farmers", 3, 5)],
            DirectSameAsSummary = true,
            TargetBeneficiaries =
            [
                new(null, InvestmentProposalBeneficiarySection.Direct, "Ignored while on", 9, 9),
                new(null, InvestmentProposalBeneficiarySection.Indirect, "Families", 10, 12),
            ],
        });
        DocTargetBeneficiaries f = Build(dto).TargetBeneficiaries;

        Assert.Equal(["Farmers"], f.Direct.Select(r => r.Label));
        Assert.Equal(["Families"], f.Indirect.Select(r => r.Label));
        Assert.Equal(new DocBeneficiaryRow("TOTAL", 13, 17, 30), f.Total);
    }

    [Fact]
    public void TargetBeneficiaries_OwnRows_WhenSameAsSummaryIsOff()
    {
        ProposalDto dto = Proposal([], Content() with
        {
            BeneficiariesSummary = [new(null, "Farmers", 3, 5)],
            DirectSameAsSummary = false,
            TargetBeneficiaries = [new(null, InvestmentProposalBeneficiarySection.Direct, "Barangay officials", 1, 1)],
        });
        Assert.Equal(["Barangay officials"], Build(dto).TargetBeneficiaries.Direct.Select(r => r.Label));
    }

    // ── D, E: the template's labels ───────────────────────────────────────────

    [Fact]
    public void Benefits_AreTheFiveSectorsInTemplateOrder()
        => Assert.Equal(
            ["Social", "Economic", "Environmental", "Institutional", "Infrastructure/Land Use"],
            Build(Proposal([])).Benefits.Select(b => b.Sector));

    [Fact]
    public void Logframe_UsesTheTemplateLabels_NotTheStoredCodes()
        => Assert.Equal(
            ["Impact", "Outcome", "Output", "Input/Activities"],
            Build(Proposal([])).Logframe.Select(l => l.Label));

    // ── G and H-1: one grouping and one order ─────────────────────────────────

    private static ProposalDto Grouped()
    {
        ProposalAipRowDto[] rows =
        [
            Activity(1, "Orientation", Line("TEV", mooe: 100m)),
            Activity(2, "Workshop",    Line("Meals", mooe: 200m)),
            Activity(3, "Printing",    Line("Supplies", mooe: 300m)),
        ];
        return Proposal(rows, Content() with
        {
            Groups =
            [
                new(1, "g1", "Capability Building"),
                new(2, "g2", "Empty group"),
                new(3, "g3", "Planning"),
            ],
            WorkPlan =
            [
                AipRow(3),                       // ungrouped, but listed first
                AipRow(2, "g3"),
                Step("Preparation of Travel Order", "g1"),
                AipRow(1, "g1"),
                Step("Liquidation"),             // ungrouped
            ],
        });
    }

    [Fact]
    public void WorkPlan_GroupsInGroupOrder_UngroupedAfter_EmptyGroupsSkipped()
    {
        IReadOnlyList<DocWorkPlanLine> g = Build(Grouped()).WorkPlan;

        Assert.Equal(
            ["Capability Building", "Preparation of Travel Order", "Orientation",
             "Planning", "Workshop",
             "Printing", "Liquidation"],
            g.Select(l => l.Text));
        Assert.DoesNotContain(g, l => l.Text == "Empty group");
        Assert.Equal([true, false, false, true, false, false, false], g.Select(l => l.IsGroupHeader));
    }

    [Fact]
    public void WorkPlan_RowsNumberedStraightThrough_GroupHeadersUnnumbered()
        => Assert.Equal([null, 1, 2, null, 3, 4, 5], Build(Grouped()).WorkPlan.Select(l => l.Number));

    [Fact]
    public void WorkPlan_NoGroups_RowsPrintFlatInWorkPlanOrder()
    {
        ProposalDto dto = Proposal([Activity(1, "A", Line("TEV", mooe: 1m)), Activity(2, "B", Line("TEV", mooe: 1m))],
            Content() with { WorkPlan = [AipRow(1), AipRow(2)] });
        Assert.Equal(["A", "B"], Build(dto).WorkPlan.Select(l => l.Text));
    }

    [Fact]
    public void AnnexH1_FollowsTheSameGroupingAndOrderAsG()
    {
        ProposalDocument doc = Build(Grouped());
        Assert.Equal(doc.WorkPlan.Select(l => (l.IsGroupHeader, l.Text)), doc.AnnexH1.Lines.Select(l => (l.IsGroupHeader, l.Text)));
    }

    [Fact]
    public void WorkPlan_AipRows_TakeTimelineAndOprFromTheAip_StepsKeepTheirOwn()
    {
        IReadOnlyList<DocWorkPlanLine> g = Build(Grouped()).WorkPlan;
        DocWorkPlanLine orientation = g.Single(l => l.Text == "Orientation");
        DocWorkPlanLine step = g.Single(l => l.Text == "Liquidation");

        Assert.Equal(("January–December 2028", "PPDO"), (orientation.Timeline, orientation.Opr));
        Assert.Equal(("Quarterly", "PPDO", "Target", "Issue"), (step.Timeline, step.Opr, step.PerformanceTarget, step.GenderIssues));
    }

    [Fact]
    public void WorkPlan_AnActivityMissingFromTheWorkPlan_IsAppendedUngrouped()
    {
        // The service always lists every activity; the builder must not drop one if it didn't.
        ProposalDto dto = Proposal([Activity(1, "A", Line("TEV", mooe: 1m)), Activity(2, "B", Line("TEV", mooe: 2m))],
            Content() with { WorkPlan = [AipRow(1)] });
        ProposalDocument doc = Build(dto);

        Assert.Equal(["A", "B"], doc.WorkPlan.Select(l => l.Text));
        Assert.Equal(3m, doc.AnnexH1.GrandTotal);
    }

    [Fact]
    public void WorkPlan_ARowForAnActivityNoLongerInTheAip_IsNotPrinted()
    {
        ProposalDto dto = Proposal([Activity(1, "A", Line("TEV", mooe: 1m))],
            Content() with { WorkPlan = [AipRow(1), AipRow(99)] });
        Assert.Equal(["A"], Build(dto).WorkPlan.Select(l => l.Text));
    }

    [Fact]
    public void AnnexH1_ProposalOnlyRows_HaveNoMoneyAndNoFund()
    {
        DocAnnexLine step = Build(Grouped()).AnnexH1.Lines.Single(l => l.Text == "Liquidation");
        Assert.Equal((null, null, null, null, null), (step.Mooe, step.Ps, step.Co, step.Total, step.SourceOfFund));
    }

    [Fact]
    public void AnnexH1_GroupHeaders_CarryNoAmounts()
    {
        DocAnnexLine header = Build(Grouped()).AnnexH1.Lines.First(l => l.IsGroupHeader);
        Assert.Equal((null, null, null, null, null), (header.Mooe, header.Ps, header.Co, header.Total, header.SourceOfFund));
    }

    // ── H-1 money cells ───────────────────────────────────────────────────────

    [Fact]
    public void ComputationLine_WithDays()
    {
        ProposalDto dto = Proposal([Activity(1, "Mangrove Restoration",
            Line("Meals", mooe: 35_460m, items: [Item("Lunch", 394m, 30m, "pax", 3m)]))]);
        DocAnnexBlock block = Build(dto).AnnexH1.Lines.Single().Mooe!.Blocks.Single();

        Assert.Equal("Meals:", block.Heading);
        Assert.Equal(["Lunch  394.00 x 30 pax x 3 days = 35,460.00"], block.Lines);
        Assert.Null(block.Amount);
    }

    [Fact]
    public void ComputationLine_OneDay_OmitsTheDaysPart()
    {
        ProposalDto dto = Proposal([Activity(1, "A",
            Line("Fuel", mooe: 4_950m, items: [Item("Fuel", 75m, 66m, "liters", 1m)]))]);
        Assert.Equal(["Fuel  75.00 x 66 liters = 4,950.00"], Build(dto).AnnexH1.Lines.Single().Mooe!.Blocks.Single().Lines);
    }

    [Fact]
    public void ComputationLine_FractionalQuantity_AndNoUnit()
    {
        ProposalItemDto item = new("Rental", 1_500m, 2.5m, " ", 2m, 7_500m);
        ProposalDto dto = Proposal([Activity(1, "A", Line("Rent", mooe: 7_500m, items: [item]))]);
        Assert.Equal(["Rental  1,500.00 x 2.5 x 2 days = 7,500.00"], Build(dto).AnnexH1.Lines.Single().Mooe!.Blocks.Single().Lines);
    }

    [Fact]
    public void Expenditure_WithoutItems_PrintsHeadingAndAmount()
    {
        ProposalDto dto = Proposal([Activity(1, "A", Line("Planting Stocks and Other Materials", mooe: 25_000m))]);
        DocAnnexBlock block = Build(dto).AnnexH1.Lines.Single().Mooe!.Blocks.Single();

        Assert.Equal("Planting Stocks and Other Materials:", block.Heading);
        Assert.Empty(block.Lines);
        Assert.Equal(25_000m, block.Amount);
    }

    [Fact]
    public void Expenditure_GoesInItsOwnClassColumn_EachCellEndsWithItsClassTotal()
    {
        ProposalDto dto = Proposal([Activity(1, "A",
            Line("TEV", mooe: 1_000m),
            Line("Office Supplies", mooe: 500m),
            Line("Honoraria", ps: 2_000m),
            Line("Drone", co: 200_000m))]);
        DocAnnexLine row = Build(dto).AnnexH1.Lines.Single();

        Assert.Equal(["TEV:", "Office Supplies:"], row.Mooe!.Blocks.Select(b => b.Heading));
        Assert.Equal(1_500m, row.Mooe.Total);
        Assert.Equal(["Honoraria:"], row.Ps!.Blocks.Select(b => b.Heading));
        Assert.Equal(["Drone:"], row.Co!.Blocks.Select(b => b.Heading));
        Assert.Equal(203_500m, row.Total);
    }

    [Fact]
    public void Expenditure_AccountTitleAlreadyEndingInAColon_GetsNoSecondColon()
    {
        ProposalDto dto = Proposal([Activity(1, "A", Line("TEV:", mooe: 1m))]);
        Assert.Equal("TEV:", Build(dto).AnnexH1.Lines.Single().Mooe!.Blocks.Single().Heading);
    }

    [Fact]
    public void EmptyClass_IsABlankCell()
    {
        DocAnnexLine row = Build(Proposal([Activity(1, "A", Line("TEV", mooe: 1m))])).AnnexH1.Lines.Single();
        Assert.Null(row.Ps);
        Assert.Null(row.Co);
    }

    [Fact]
    public void Activity_WithoutLines_PrintsOneTotalsRow()
    {
        DocAnnexLine row = Build(Proposal([Bare(1, "Synthetic", ps: 0m, mooe: 5_000m, co: 0m)])).AnnexH1.Lines.Single();

        Assert.Empty(row.Mooe!.Blocks);
        Assert.Equal(5_000m, row.Mooe.Total);
        Assert.Null(row.Ps);
        Assert.Equal(5_000m, row.Total);
        Assert.Equal("General Fund", row.SourceOfFund);
    }

    [Fact]
    public void SourceOfFund_PrintsTheFundNames()
    {
        ProposalDto dto = Proposal([Activity(1, "A",
            Line("TEV", mooe: 1m, fund: "General Fund"),
            Line("Drone", co: 2m, fund: "20% Development Fund"))]);
        Assert.Equal("General Fund, 20% Development Fund", Build(dto).AnnexH1.Lines.Single().SourceOfFund);
    }

    [Fact]
    public void AnnexH1_TotalRow_SumsEachClassAndTheActivityTotals()
    {
        DocAnnexH1 h = Build(Proposal([
            Activity(1, "A", Line("TEV", mooe: 100.25m), Line("Honoraria", ps: 50m)),
            Bare(2, "B", ps: 0m, mooe: 0m, co: 1_000m),
        ])).AnnexH1;

        Assert.Equal((100.25m, 50m, 1_000m, 1_150.25m), (h.MooeTotal, h.PsTotal, h.CoTotal, h.GrandTotal));
    }

    [Fact]
    public void AnnexH1_GrandTotal_EqualsSectionAProjectCost_ToTheCentavo()
    {
        // Decision 8's invariant, on a realistic mix: itemised, single-amount, multi-class and bare.
        ProposalDto dto = Proposal([
            Activity(1, "Mangrove Restoration",
                Line("Meals", mooe: 33_200m, items: [Item("Breakfast", 295m, 30m, "pax", 2m), Item("Snacks AM", 155m, 50m, "pax", 2m)]),
                Line("Accommodation", mooe: 58_016m, items: [Item("Accommodation", 2_072m, 14m, "rooms", 2m)]),
                Line("Planting Stocks and Other Materials", mooe: 25_000m)),
            Activity(2, "Monitoring", Line("Honoraria", ps: 12_345.67m), Line("Drone", co: 200_000m)),
            Bare(3, "Synthetic", ps: 0.01m, mooe: 999.99m, co: 0m),
        ]);
        ProposalDocument doc = Build(dto);

        Assert.Equal(dto.Header.ProjectCost, doc.AnnexH1.GrandTotal);
        Assert.Equal(doc.SectionA.ProjectCost, doc.AnnexH1.GrandTotal);
    }

    // ── I ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Trainings_ListEveryMember_AndMergeIdenticalNeighbours()
    {
        ProposalDto dto = Proposal([], Content() with
        {
            TeamMembers =
            [
                new(null, "Ana",   "F", null, null, "Gender Sensitivity Training"),
                new(null, "Ben",   "M", null, null, "Gender Sensitivity Training"),
                new(null, "Carlo", "M", null, null, "GAD Planning"),
                new(null, "Dina",  "F", null, null, null),
                new(null, "Ed",    "M", null, null, null),
                new(null, "Fe",    "F", null, null, "Gender Sensitivity Training"),
            ],
        });
        IReadOnlyList<DocTrainingRow> t = Build(dto).Team.Trainings;

        Assert.Equal(["Ana", "Ben", "Carlo", "Dina", "Ed", "Fe"], t.Select(r => r.Member));
        // Empty trainings never merge; the same training further down starts a new run.
        Assert.Equal([2, 0, 1, 1, 1, 1], t.Select(r => r.RowSpan));
    }

    // ── K, L, M, signatures ───────────────────────────────────────────────────

    [Fact]
    public void Monitoring_AlwaysPrintsTheThreePhases_InOrder()
    {
        ProposalDto dto = Proposal([], Content() with
        {
            Monitoring = [new(null, InvestmentProposalMonitoringPhase.Post, "Evaluation", null, null)],
        });
        IReadOnlyList<DocMonitoringPhase> k = Build(dto).Monitoring;

        Assert.Equal(["PRE-IMPLEMENTATION", "DURING IMPLEMENTATION", "POST-IMPLEMENTATION"], k.Select(p => p.Label));
        Assert.Equal([0, 0, 1], k.Select(p => p.Rows.Count));
    }

    [Fact]
    public void ClimateTypology_PrintsNA_WhenThereIsNone()
    {
        Assert.Equal("N/A", Build(Proposal([], climate: "")).ClimateTypology);
        Assert.Equal("A1 – Mitigation", Build(Proposal([], climate: "A1 – Mitigation")).ClimateTypology);
    }

    [Fact]
    public void Signatories_OnlyNamedSlots_InSlotOrder()
    {
        ProposalDto dto = Proposal([], Content() with
        {
            Signatories =
            [
                new(3, "Noted by", "Gov. Name", "Local Chief Executive"),
                new(1, "Prepared by", "Ana", "Planning Officer"),
                new(2, "Submitted by", "  ", "PPDC"),
                new(4, null, "Extra", null),
            ],
        });
        IReadOnlyList<DocSignatory> s = Build(dto).Signatories;

        Assert.Equal([1, 3, 4], s.Select(x => x.Slot));
        Assert.Equal(string.Empty, s.Single(x => x.Slot == 4).Label);
    }

    // ── Rich text ─────────────────────────────────────────────────────────────

    [Fact]
    public void RichText_Paragraphs_WithBoldAndItalicRuns()
    {
        ProposalDto dto = Proposal([], Content() with { Rationale = "<p>Plain <strong>bold <em>both</em></strong> end</p><p>Two</p>" });
        RichTextContent r = Build(dto).Rationale;

        Assert.Equal(2, r.Blocks.Count);
        Assert.Equal(
            [("Plain ", false, false), ("bold ", true, false), ("both", true, true), (" end", false, false)],
            r.Blocks[0].Runs.Select(x => (x.Text, x.Bold, x.Italic)));
        Assert.Equal(RichBlockKind.Paragraph, r.Blocks[1].Kind);
    }

    [Fact]
    public void RichText_Lists_NumberedPerList_WithNesting()
    {
        ProposalDto dto = Proposal([], Content() with
        {
            Description = "<ol><li><p>First</p></li><li>Second<ul><li>Sub</li></ul></li></ol><ol><li>Again</li></ol>",
        });
        IReadOnlyList<RichBlock> b = Build(dto).Description.Blocks;

        Assert.Equal(
            [(RichBlockKind.Numbered, 0, (int?)1, "First"), (RichBlockKind.Numbered, 0, 2, "Second"),
             (RichBlockKind.Bullet, 1, null, "Sub"), (RichBlockKind.Numbered, 0, 1, "Again")],
            b.Select(x => (x.Kind, x.Level, x.Number, string.Concat(x.Runs.Select(r => r.Text)))));
    }

    [Fact]
    public void RichText_LineBreaks_AreBreakRuns()
    {
        // FromPlainText is how the AIP description arrives: one <p> with <br>s.
        ProposalDto dto = Proposal([], Content() with { Description = ProposalRichText.FromPlainText("Line one\nLine two") });
        IReadOnlyList<RichRun> runs = Build(dto).Description.Blocks.Single().Runs;

        Assert.Equal(["Line one", "", "Line two"], runs.Select(r => r.Text));
        Assert.True(runs[1].IsBreak);
    }

    [Fact]
    public void RichText_IsSanitizedAgain_AndDecodesEntities()
    {
        ProposalDto dto = Proposal([], Content() with { Rationale = "<p>A &amp; B</p><table><tr><td>cell</td></tr></table><script>bad()</script>" });
        RichTextContent r = Build(dto).Rationale;

        Assert.Equal("A & B", string.Concat(r.Blocks[0].Runs.Select(x => x.Text)));
        Assert.DoesNotContain(r.Blocks.SelectMany(x => x.Runs), x => x.Text.Contains("bad()"));
    }

    [Fact]
    public void RichText_Blank_IsEmpty()
    {
        Assert.True(Build(Proposal([])).Description.IsEmpty);
        Assert.True(Build(Proposal([], Content() with { Rationale = "<p> </p><p><br></p>" })).Rationale.IsEmpty);
    }

    [Fact]
    public void RichText_TextOutsideAParagraph_BecomesAParagraph()
    {
        ProposalDto dto = Proposal([], Content() with { GeneralObjective = "Loose <strong>text</strong>" });
        RichBlock block = Build(dto).GeneralObjective.Blocks.Single();
        Assert.Equal((RichBlockKind.Paragraph, "Loose text"), (block.Kind, string.Concat(block.Runs.Select(r => r.Text))));
    }
}
