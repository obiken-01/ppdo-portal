using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using PPDO.Application.DTOs.InvestmentProposal;
using PPDO.Application.DTOs.InvestmentProposal.Document;

namespace PPDO.Application.Common;

/// <summary>
/// Builds the investment proposal as it prints (v1.8.0 Demo 2.15 — PPDO-157,
/// Investment_Proposal_Spec.md decisions 6, 10, 13–21, 30 and 31).
///
/// <para>
/// ⚠️ <b>One builder, one order.</b> Section G and Annex H-1 iterate the same grouping
/// (<see cref="Arrange"/>), so the two can never list the activities differently. The Word renderer
/// (PPDO-158) writes this model as-is; numbering, totals, merged cells and wording are decided here,
/// where they can be tested without opening a document.
/// </para>
///
/// <para>
/// Pure, like <see cref="AipFormRowBuilder"/>: no repository, no scope, no Open XML. Its input is the
/// <see cref="ProposalDto"/> the service already built, so a Final proposal prints its snapshot
/// and a Draft prints the live AIP without this class knowing which. Amounts are copied exactly as
/// encoded: never rounded, never +30% (decision 7).
/// </para>
/// </summary>
public static class InvestmentProposalDocumentBuilder
{
    public const string Title    = "PGOM INVESTMENT PROPOSAL";   // decision 3: PPDC's title, not the template's
    public const string TotalRow = "TOTAL";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Template labels for stored codes (decision 30).
    private static readonly IReadOnlyDictionary<string, string> LogframeLabels = new Dictionary<string, string>
    {
        [InvestmentProposalLogframeLevel.Impact]  = "Impact",
        [InvestmentProposalLogframeLevel.Outcome] = "Outcome",
        [InvestmentProposalLogframeLevel.Output]  = "Output",
        [InvestmentProposalLogframeLevel.Input]   = "Input/Activities",
    };

    private static readonly IReadOnlyDictionary<string, string> PhaseLabels = new Dictionary<string, string>
    {
        [InvestmentProposalMonitoringPhase.Pre]    = "PRE-IMPLEMENTATION",
        [InvestmentProposalMonitoringPhase.During] = "DURING IMPLEMENTATION",
        [InvestmentProposalMonitoringPhase.Post]   = "POST-IMPLEMENTATION",
    };

    public static ProposalDocument Build(ProposalDto proposal)
    {
        ProposalHeaderDto  h = proposal.Header;
        ProposalContentDto c = proposal.Content;

        List<DocBeneficiaryRow> summary = c.BeneficiariesSummary
            .Select(b => Beneficiary(b.Indicator, b.Male, b.Female)).ToList();

        IReadOnlyList<Arranged> arranged = Arrange(c, proposal.AipRows);

        return new ProposalDocument(
            Title,
            FileName(h),
            new DocSectionA(
                h.ProgramTitle, h.ProjectTitle, h.Proponent,
                ProjectType: string.Empty,
                Text(c.ProjectLocation), h.ScheduleStart, h.ScheduleEnd,
                h.ProjectCost, h.AttributedGadBudget,
                string.Join(", ", h.FundingSources),
                ChecklistName(c.HgdgChecklist), c.HgdgScore,
                new DocBeneficiaryTable(summary, TotalOf(summary))),
            RichText(c.Description),
            RichText(c.Rationale),
            InvestmentProposalSector.All
                .Select(s => c.Benefits.FirstOrDefault(b => b.Sector == s) is { } b
                    ? new DocBenefitRow(s, RichText(b.Benefit), RichText(b.Cost))
                    : new DocBenefitRow(s, RichTextContent.Empty, RichTextContent.Empty))
                .ToList(),
            RichText(c.GeneralObjective),
            InvestmentProposalLogframeLevel.All
                .Select(l => c.Logframe.FirstOrDefault(x => x.Level == l) is { } x
                    ? new DocLogframeRow(LogframeLabels[l], Text(x.Target), Text(x.Verification))
                    : new DocLogframeRow(LogframeLabels[l], null, null))
                .ToList(),
            TargetBeneficiaries(c, summary),
            WorkPlan(arranged),
            AnnexH1(arranged),
            Team(c),
            RichText(c.PartnershipSustainability),
            InvestmentProposalMonitoringPhase.All
                .Select(phase => new DocMonitoringPhase(PhaseLabels[phase], c.Monitoring
                    .Where(m => m.Phase == phase && !string.IsNullOrWhiteSpace(m.Activity))
                    .Select(m => new DocMonitoringRow(m.Activity!.Trim(), Text(m.Schedule), Text(m.Tools)))
                    .ToList()))
                .ToList(),
            c.Risks.Where(r => !string.IsNullOrWhiteSpace(r.Risk))
                .Select(r => new DocRiskRow(r.Risk!.Trim(), Text(r.Prevention), Text(r.Monitoring)))
                .ToList(),
            Text(c.WomensImpactStrategy),
            string.IsNullOrWhiteSpace(h.ClimateTypology) ? "N/A" : h.ClimateTypology.Trim(),
            c.Signatories
                .Where(s => !string.IsNullOrWhiteSpace(s.Name))
                .OrderBy(s => s.Slot)
                .Select(s => new DocSignatory(s.Slot, Text(s.Label) ?? string.Empty, s.Name!.Trim(), Text(s.Position)))
                .ToList());
    }

    // ── File name ─────────────────────────────────────────────────────────────

    private static readonly Regex Unsafe = new(@"[\\/:*?""<>|\x00-\x1F]", RegexOptions.Compiled);
    private const int MaxFileNameLength = 115;   // + ".docx" = 120, the export's cap

    /// <summary>"Investment Proposal - {RefCode} - {Name}.docx" (spec §10), safe for every OS, at most 120 characters.</summary>
    public static string FileName(ProposalHeaderDto header)
    {
        string stem = $"Investment Proposal - {header.ProjectRefCode} - {header.ProjectTitle}";
        stem = Regex.Replace(Unsafe.Replace(stem, "_"), @"\s+", " ").Trim().TrimEnd('.');
        if (stem.Length > MaxFileNameLength) stem = stem[..MaxFileNameLength].TrimEnd();
        return stem + ".docx";
    }

    // ── Beneficiaries (A, F) ──────────────────────────────────────────────────

    private static DocBeneficiaryRow Beneficiary(string? label, int? male, int? female)
        => new(label?.Trim() ?? string.Empty, male, female, male is null && female is null ? null : (male ?? 0) + (female ?? 0));

    /// <summary>The TOTAL row. A column with no counts at all stays blank rather than printing 0.</summary>
    private static DocBeneficiaryRow TotalOf(IReadOnlyList<DocBeneficiaryRow> rows)
    {
        int? Sum(Func<DocBeneficiaryRow, int?> pick)
            => rows.Any(r => pick(r) is not null) ? rows.Sum(r => pick(r) ?? 0) : null;
        return new DocBeneficiaryRow(TotalRow, Sum(r => r.Male), Sum(r => r.Female), Sum(r => r.Total));
    }

    private static DocTargetBeneficiaries TargetBeneficiaries(ProposalContentDto c, IReadOnlyList<DocBeneficiaryRow> summary)
    {
        List<DocBeneficiaryRow> Rows(string kind) => c.TargetBeneficiaries
            .Where(t => t.Kind == kind)
            .Select(t => Beneficiary(t.Name, t.Male, t.Female))
            .ToList();

        // Decision 10: while "Same as Section A" is on, F-Direct is A's rows and its own are ignored.
        IReadOnlyList<DocBeneficiaryRow> direct = c.DirectSameAsSummary ? summary : Rows(InvestmentProposalBeneficiarySection.Direct);
        List<DocBeneficiaryRow> indirect = Rows(InvestmentProposalBeneficiarySection.Indirect);
        return new DocTargetBeneficiaries(direct, indirect, TotalOf([.. direct, .. indirect]));
    }

    // ── G and H-1: one arrangement ────────────────────────────────────────────

    /// <summary>A group header (<see cref="GroupLabel"/>) or a row, with its AIP activity when it has one.</summary>
    private sealed record Arranged(string? GroupLabel, ProposalWorkPlanRowDto? Row, ProposalAipRowDto? Activity);

    /// <summary>
    /// Decision 14: groups in their order, each followed by its rows in work-plan order; empty groups
    /// skipped; ungrouped rows after every group. A row for an activity the AIP no longer has is
    /// dropped; an activity with no row (the service always sends one) is appended ungrouped.
    /// </summary>
    private static IReadOnlyList<Arranged> Arrange(ProposalContentDto c, IReadOnlyList<ProposalAipRowDto> aipRows)
    {
        Dictionary<int, ProposalAipRowDto> byId = aipRows.ToDictionary(a => a.ActivityId);

        List<ProposalWorkPlanRowDto> rows = c.WorkPlan
            .Where(r => r.AipActivityId is int id ? byId.ContainsKey(id) : !string.IsNullOrWhiteSpace(r.Name))
            .ToList();
        HashSet<int> listed = rows.Where(r => r.AipActivityId is not null).Select(r => r.AipActivityId!.Value).ToHashSet();
        rows.AddRange(aipRows.Where(a => !listed.Contains(a.ActivityId))
            .Select(a => new ProposalWorkPlanRowDto(null, a.ActivityId, null, null, null, null, null, null)));

        HashSet<string> groupKeys = c.Groups.Where(g => g.ClientKey is not null).Select(g => g.ClientKey!).ToHashSet(StringComparer.Ordinal);
        Arranged Of(ProposalWorkPlanRowDto r)
            => new(null, r, r.AipActivityId is int id ? byId[id] : null);

        List<Arranged> result = [];
        foreach (ProposalGroupDto g in c.Groups)
        {
            List<ProposalWorkPlanRowDto> members = rows.Where(r => r.GroupKey is not null && r.GroupKey == g.ClientKey).ToList();
            if (members.Count == 0) continue;
            result.Add(new Arranged(Text(g.Label) ?? string.Empty, null, null));
            result.AddRange(members.Select(Of));
        }
        // A key that names no group counts as ungrouped, so the row is never lost.
        result.AddRange(rows.Where(r => r.GroupKey is null || !groupKeys.Contains(r.GroupKey)).Select(Of));
        return result;
    }

    private static string RowName(Arranged a)
        => a.Activity?.Name.Trim() ?? a.Row!.Name!.Trim();

    private static IReadOnlyList<DocWorkPlanLine> WorkPlan(IReadOnlyList<Arranged> arranged)
    {
        List<DocWorkPlanLine> lines = [];
        int number = 0;
        foreach (Arranged a in arranged)
        {
            if (a.GroupLabel is string label)
            {
                lines.Add(new DocWorkPlanLine(true, null, label, null, null, null, null));
                continue;
            }
            ProposalWorkPlanRowDto r = a.Row!;
            // Decision 16: an AIP row's timeline and OPR are the AIP's; a step's are typed.
            (string? timeline, string? opr) = a.Activity is { } act ? (act.Timeline, act.Opr) : (Text(r.Timeline), Text(r.Opr));
            lines.Add(new DocWorkPlanLine(false, ++number, RowName(a),
                Text(r.PerformanceTarget), Text(r.GenderIssues), timeline, opr));
        }
        return lines;
    }

    private static DocAnnexH1 AnnexH1(IReadOnlyList<Arranged> arranged)
    {
        List<DocAnnexLine> lines = [];
        foreach (Arranged a in arranged)
        {
            if (a.GroupLabel is string label)
                lines.Add(new DocAnnexLine(true, label, null, null, null, null, null));
            else if (a.Activity is not { } act)
                lines.Add(new DocAnnexLine(false, RowName(a), null, null, null, null, null));   // decision 15
            else
                lines.Add(new DocAnnexLine(false, RowName(a),
                    Cell(act, e => e.Mooe, act.Mooe), Cell(act, e => e.Ps, act.Ps), Cell(act, e => e.Co, act.Co),
                    act.Total, act.FundNames.Count == 0 ? null : string.Join(", ", act.FundNames)));
        }

        List<ProposalAipRowDto> activities = arranged.Where(a => a.Activity is not null).Select(a => a.Activity!).ToList();
        return new DocAnnexH1(lines,
            activities.Sum(a => a.Mooe), activities.Sum(a => a.Ps), activities.Sum(a => a.Co), activities.Sum(a => a.Total));
    }

    /// <summary>
    /// One money cell (decision 17): a block per expenditure of that class, then the class total.
    /// The total is the activity's own figure, the one Section A sums, so the H-1 total and the
    /// Project Cost agree by construction (decision 8). A class with nothing in it is a blank cell.
    /// </summary>
    private static DocAnnexCell? Cell(ProposalAipRowDto activity, Func<ProposalExpenditureDto, decimal> amountOf, decimal classTotal)
    {
        List<DocAnnexBlock> blocks = activity.Expenditures
            .Where(e => amountOf(e) != 0m)
            .Select(e => Block(e, amountOf(e)))
            .ToList();
        return blocks.Count == 0 && classTotal == 0m ? null : new DocAnnexCell(blocks, classTotal);
    }

    private static DocAnnexBlock Block(ProposalExpenditureDto e, decimal amount)
    {
        string title = Text(e.AccountTitle) ?? Text(e.AccountCode) ?? "Other";
        string heading = title.EndsWith(':') ? title : title + ":";

        // The computation lines belong to the expenditure as a whole. Split across classes, they
        // would not add up to either amount, so such a line prints its amount instead.
        int classes = new[] { e.Ps, e.Mooe, e.Co }.Count(x => x != 0m);
        return e.Items.Count > 0 && classes == 1
            ? new DocAnnexBlock(heading, e.Items.Select(ComputationLine).ToList(), null)
            : new DocAnnexBlock(heading, [], amount);
    }

    /// <summary>
    /// "{Name}  {UnitPrice:N2} x {Qty} {Unit}[ x {NumberOfDays} days] = {LineTotal:N2}" (decision 17).
    /// The days part only when NumberOfDays ≠ 1.
    /// </summary>
    public static string ComputationLine(ProposalItemDto item)
    {
        string qty  = Count(item.Qty);
        string unit = Text(item.Unit) is string u ? $" {u}" : string.Empty;
        string days = item.NumberOfDays != 1m ? $" x {Count(item.NumberOfDays)} days" : string.Empty;
        return $"{item.Name.Trim()}  {Money(item.UnitPrice)} x {qty}{unit}{days} = {Money(item.LineTotal)}";
    }

    /// <summary>Pesos as the samples print them: "35,460.00".</summary>
    public static string Money(decimal amount) => amount.ToString("N2", Invariant);

    /// <summary>A quantity without trailing zeros: 30, 2.5.</summary>
    private static string Count(decimal value) => value.ToString("#,0.####", Invariant);

    // ── I ─────────────────────────────────────────────────────────────────────

    private static DocTeam Team(ProposalContentDto c)
    {
        List<ProposalTeamMemberDto> members = c.TeamMembers.Where(m => !string.IsNullOrWhiteSpace(m.Name)).ToList();

        // Decision 31: every member gets a training row; a run of identical non-empty trainings
        // prints as one merged cell.
        List<DocTrainingRow> trainings = [];
        for (int i = 0; i < members.Count;)
        {
            string? training = Text(members[i].RequiredTraining);
            int run = 1;
            if (training is not null)
                while (i + run < members.Count && Text(members[i + run].RequiredTraining) == training) run++;
            for (int k = 0; k < run; k++)
                trainings.Add(new DocTrainingRow(members[i + k].Name!.Trim(), training, k == 0 ? run : 0));
            i += run;
        }

        return new DocTeam(
            Text(c.ProjectSupervisor),
            Text(c.ProjectManager),
            members.Select(m => new DocTeamMember(m.Name!.Trim(), m.Sex ?? string.Empty, Text(m.GadTrainings), Text(m.Expertise))).ToList(),
            trainings);
    }

    // ── Rich text ─────────────────────────────────────────────────────────────

    private static readonly HtmlParser Parser = new();

    /// <summary>
    /// The decision-20 HTML as blocks. Sanitized again first: stored text already is, but the
    /// renderer must never depend on that having happened.
    /// </summary>
    public static RichTextContent RichText(string? html)
    {
        if (ProposalRichText.Sanitize(html) is not string clean) return RichTextContent.Empty;

        IElement body = Parser.ParseDocument($"<body>{clean}</body>").Body!;
        List<RichBlock> blocks = [];
        List<RichRun> loose = [];   // text sitting directly in the body, outside any <p>

        void FlushLoose()
        {
            AddBlock(blocks, RichBlockKind.Paragraph, 0, null, loose);
            loose = [];
        }

        foreach (INode node in body.ChildNodes)
        {
            switch ((node as IElement)?.LocalName)
            {
                case "p":
                    FlushLoose();
                    AddBlock(blocks, RichBlockKind.Paragraph, 0, null, Runs(node, false, false).ToList());
                    break;
                case "ul" or "ol":
                    FlushLoose();
                    AddList(blocks, (IElement)node, 0);
                    break;
                default:
                    loose.AddRange(Runs(node, false, false));
                    break;
            }
        }
        FlushLoose();
        return blocks.Count == 0 ? RichTextContent.Empty : new RichTextContent(blocks);
    }

    private static void AddList(List<RichBlock> blocks, IElement list, int level)
    {
        bool numbered = list.LocalName == "ol";
        int n = 0;
        foreach (IElement li in list.Children.Where(e => e.LocalName == "li"))
        {
            n++;
            List<RichRun> runs = [];
            List<IElement> nested = [];
            foreach (INode child in li.ChildNodes)
            {
                if (child is IElement { LocalName: "ul" or "ol" } sub) { nested.Add(sub); continue; }
                // TipTap wraps an item's text in <p>; a second paragraph in one item is a line break.
                if (child is IElement { LocalName: "p" } && runs.Count > 0) runs.Add(new RichRun(string.Empty, false, false, IsBreak: true));
                runs.AddRange(Runs(child, false, false));
            }
            AddBlock(blocks, numbered ? RichBlockKind.Numbered : RichBlockKind.Bullet, level, numbered ? n : null, runs, keepEmpty: true);
            foreach (IElement sub in nested) AddList(blocks, sub, level + 1);
        }
    }

    private static IEnumerable<RichRun> Runs(INode node, bool bold, bool italic)
    {
        if (node is IText text)
        {
            string value = Whitespace.Replace(text.Data, " ");
            if (value.Length > 0) yield return new RichRun(value, bold, italic);
            yield break;
        }
        if (node is not IElement el) yield break;
        if (el.LocalName == "br")
        {
            yield return new RichRun(string.Empty, bold, italic, IsBreak: true);
            yield break;
        }
        bool b = bold || el.LocalName == "strong";
        bool i = italic || el.LocalName == "em";
        foreach (INode child in el.ChildNodes)
            foreach (RichRun run in Runs(child, b, i))
                yield return run;
    }

    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Adds a block after merging same-format neighbours and trimming the outer whitespace. A block
    /// with no visible text is dropped, except a list item, which keeps its place in the numbering.
    /// </summary>
    private static void AddBlock(
        List<RichBlock> blocks, RichBlockKind kind, int level, int? number, List<RichRun> runs, bool keepEmpty = false)
    {
        List<RichRun> merged = [];
        foreach (RichRun run in runs)
        {
            if (!run.IsBreak && merged.Count > 0 && merged[^1] is { IsBreak: false } last
                && last.Bold == run.Bold && last.Italic == run.Italic)
                merged[^1] = last with { Text = last.Text + run.Text };
            else
                merged.Add(run);
        }

        // Trim: leading/trailing breaks go, then the outer spaces of the first and last text runs.
        while (merged.Count > 0 && merged[0].IsBreak) merged.RemoveAt(0);
        while (merged.Count > 0 && merged[^1].IsBreak) merged.RemoveAt(merged.Count - 1);
        if (merged.Count > 0) merged[0] = merged[0] with { Text = merged[0].Text.TrimStart() };
        if (merged.Count > 0) merged[^1] = merged[^1] with { Text = merged[^1].Text.TrimEnd() };
        // Around a break, the spaces are invisible in print.
        for (int k = 0; k < merged.Count; k++)
        {
            if (merged[k].IsBreak) continue;
            if (k > 0 && merged[k - 1].IsBreak) merged[k] = merged[k] with { Text = merged[k].Text.TrimStart() };
            if (k < merged.Count - 1 && merged[k + 1].IsBreak) merged[k] = merged[k] with { Text = merged[k].Text.TrimEnd() };
        }
        merged.RemoveAll(r => !r.IsBreak && r.Text.Length == 0);

        if (!keepEmpty && !merged.Any(r => !r.IsBreak)) return;
        blocks.Add(new RichBlock(kind, level, number, merged));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? ChecklistName(string? code)
        => Text(code) is string c ? HgdgChecklists.All.FirstOrDefault(x => x.Code == c)?.Name ?? c : null;

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
