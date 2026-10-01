namespace PPDO.Application.DTOs.InvestmentProposal.Document;

// The investment proposal as it prints (v1.8.0 Demo 2.15 — PPDO-157). Built once by
// InvestmentProposalDocumentBuilder from a ProposalDto, and written as-is by the Word renderer
// (PPDO-158), which decides fonts and borders but never order, numbering, totals or wording.
//
// Labels are the template's, word for word (decision 30). Amounts are full pesos, never rounded
// and never +30% (decision 7); the renderer formats them. A null amount or count prints blank.

/// <summary>The whole document, sections A to M and the signatures, in print order.</summary>
public sealed record ProposalDocument(
    string                              Title,
    string                              FileName,
    DocSectionA                         SectionA,
    RichTextContent                     Description,
    RichTextContent                     Rationale,
    IReadOnlyList<DocBenefitRow>        Benefits,
    RichTextContent                     GeneralObjective,
    IReadOnlyList<DocLogframeRow>       Logframe,
    DocTargetBeneficiaries              TargetBeneficiaries,
    IReadOnlyList<DocWorkPlanLine>      WorkPlan,
    DocAnnexH1                          AnnexH1,
    DocTeam                             Team,
    RichTextContent                     PartnershipSustainability,
    IReadOnlyList<DocMonitoringPhase>   Monitoring,
    IReadOnlyList<DocRiskRow>           Risks,
    string?                             WomensImpactStrategy,
    string                              ClimateTypology,
    IReadOnlyList<DocSignatory>         Signatories);

// ── A ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Section A. <see cref="ProjectType"/> is always empty (decision 6, the PDC asked for it blank).
/// <see cref="HgdgChecklist"/> is the checklist's name, not its stored code.
/// </summary>
public sealed record DocSectionA(
    string             ProgramTitle,
    string             ProjectTitle,
    string             Proponent,
    string             ProjectType,
    string?            ProjectLocation,
    string?            ScheduleStart,
    string?            ScheduleEnd,
    decimal            ProjectCost,
    decimal?           AttributedGadBudget,
    string             FundingSource,
    string?            HgdgChecklist,
    decimal?           HgdgScore,
    DocBeneficiaryTable Beneficiaries);

/// <summary>A beneficiary row. <see cref="Total"/> is Male + Female, null when both are blank.</summary>
public sealed record DocBeneficiaryRow(string Label, int? Male, int? Female, int? Total);

/// <summary>Section A's "Disaggregated Data of Intended Beneficiaries": the rows and a TOTAL row.</summary>
public sealed record DocBeneficiaryTable(IReadOnlyList<DocBeneficiaryRow> Rows, DocBeneficiaryRow Total);

// ── D, E ──────────────────────────────────────────────────────────────────────

/// <summary>One Section D row: the template's sector label, then two rich-text cells.</summary>
public sealed record DocBenefitRow(string Sector, RichTextContent Benefit, RichTextContent Cost);

/// <summary>One Section E row, labelled as the template labels it ("Input/Activities", not "Input").</summary>
public sealed record DocLogframeRow(string Label, string? Target, string? Verification);

// ── F ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Section F. <see cref="Direct"/> is Section A's rows while "Same as Section A" is on
/// (decision 10). <see cref="Total"/> sums both groups.
/// </summary>
public sealed record DocTargetBeneficiaries(
    IReadOnlyList<DocBeneficiaryRow> Direct,
    IReadOnlyList<DocBeneficiaryRow> Indirect,
    DocBeneficiaryRow                Total);

// ── G ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// One Section G line: a group header (<see cref="IsGroupHeader"/>, <see cref="Text"/> is the
/// label, no number) or a row (<see cref="Text"/> is the activity or step name). Rows are numbered
/// 1, 2, 3… straight through, ignoring groups (decision 14).
/// </summary>
public sealed record DocWorkPlanLine(
    bool    IsGroupHeader,
    int?    Number,
    string  Text,
    string? PerformanceTarget,
    string? GenderIssues,
    string? Timeline,
    string? Opr);

// ── H-1 ───────────────────────────────────────────────────────────────────────

/// <summary>Annex H-1: the lines in the same order as G, and the TOTAL row's figures.</summary>
public sealed record DocAnnexH1(
    IReadOnlyList<DocAnnexLine> Lines,
    decimal                     MooeTotal,
    decimal                     PsTotal,
    decimal                     CoTotal,
    decimal                     GrandTotal);

/// <summary>
/// One H-1 line: a group header (no amounts, no sub-total) or one activity. A proposal-only step
/// has every amount and the fund blank (decision 15). Columns are MOOE | PS | CO | Total | Source
/// of Fund (decision 17).
/// </summary>
public sealed record DocAnnexLine(
    bool         IsGroupHeader,
    string       Text,
    DocAnnexCell? Mooe,
    DocAnnexCell? Ps,
    DocAnnexCell? Co,
    decimal?     Total,
    string?      SourceOfFund);

/// <summary>
/// One money cell: the expenditure blocks of that class, then the class total. An activity with no
/// expenditure lines has no blocks, only the total (decision 18).
/// </summary>
public sealed record DocAnnexCell(IReadOnlyList<DocAnnexBlock> Blocks, decimal Total);

/// <summary>
/// One expenditure in a money cell: "TEV:" and its computation lines, or, with no procurement
/// items, the heading and <see cref="Amount"/> only.
/// </summary>
public sealed record DocAnnexBlock(string Heading, IReadOnlyList<string> Lines, decimal? Amount);

// ── I ─────────────────────────────────────────────────────────────────────────

/// <summary>Section I. <see cref="Trainings"/> lists every member, in member order (decision 31).</summary>
public sealed record DocTeam(
    string?                         ProjectSupervisor,
    string?                         ProjectManager,
    IReadOnlyList<DocTeamMember>    Members,
    IReadOnlyList<DocTrainingRow>   Trainings);

public sealed record DocTeamMember(string Name, string Sex, string? GadTrainings, string? Expertise);

/// <summary>
/// One row of the capacity-training table. Identical neighbouring trainings merge into one cell
/// (decision 31): the first row of a run has <see cref="RowSpan"/> = the run's length, the rows it
/// covers have 0. An empty training never merges.
/// </summary>
public sealed record DocTrainingRow(string Member, string? Training, int RowSpan);

// ── K, L ──────────────────────────────────────────────────────────────────────

/// <summary>One of K's three fixed phases, always printed, with its rows (possibly none).</summary>
public sealed record DocMonitoringPhase(string Label, IReadOnlyList<DocMonitoringRow> Rows);

public sealed record DocMonitoringRow(string Activity, string? Schedule, string? Tools);

public sealed record DocRiskRow(string Risk, string? Prevention, string? Monitoring);

// ── Signatures ────────────────────────────────────────────────────────────────

/// <summary>A filled signatory slot. Slots with no name are left out (decision 19).</summary>
public sealed record DocSignatory(int Slot, string Label, string Name, string? Position);

// ── Rich text ─────────────────────────────────────────────────────────────────

public enum RichBlockKind
{
    Paragraph,
    Bullet,
    Numbered,
}

/// <summary>
/// One run of text with its formatting, or a line break (<see cref="IsBreak"/>, from
/// <c>&lt;br&gt;</c>), whose <see cref="Text"/> is empty.
/// </summary>
public sealed record RichRun(string Text, bool Bold, bool Italic, bool IsBreak = false);

/// <summary>
/// A paragraph or one list item. List items carry their nesting <see cref="Level"/> (0 = top) and,
/// when numbered, their <see cref="Number"/> within their own list, so the renderer can print
/// "1." as text without Word numbering definitions.
/// </summary>
public sealed record RichBlock(RichBlockKind Kind, int Level, int? Number, IReadOnlyList<RichRun> Runs);

/// <summary>A rich-text field (decision 20) as blocks. Empty when the field is blank.</summary>
public sealed record RichTextContent(IReadOnlyList<RichBlock> Blocks)
{
    public static readonly RichTextContent Empty = new([]);

    public bool IsEmpty => Blocks.Count == 0;
}
