namespace PPDO.Domain.Entities;

/// <summary>
/// A PGOM Investment Proposal for one AIP project (v1.8.0 Demo 2.15 — PPDO-154,
/// <c>docs/v1.8/Investment_Proposal_Spec.md</c> §5).
///
/// <para>
/// ⚠️ <b>Holds only what the user types.</b> Every AIP-sourced value (titles, cost, schedule,
/// funding, the H-1 lines) is read live from the AIP while <see cref="Status"/> is <c>Draft</c>,
/// and from <see cref="SnapshotJson"/> once it is <c>Final</c> (decision 4). Nothing here mirrors an
/// AIP column.
/// </para>
///
/// <para>
/// One per project: <see cref="AipProjectId"/> is unique, and the FK is <c>NO ACTION</c> so an AIP
/// project with a proposal cannot be deleted out from under it (decision 26).
/// </para>
/// </summary>
public sealed class InvestmentProposal
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the AIP project (level 3) this proposal is for. Unique.</summary>
    public int AipProjectId { get; set; }

    /// <summary><c>Draft</c> or <c>Final</c> (<c>InvestmentProposalStatus</c>).</summary>
    public string Status { get; set; } = "Draft";

    // ── Section A ─────────────────────────────────────────────────────────────

    public string? ProjectLocation { get; set; }

    /// <summary>Code from the HGDG checklist constant list. Null = not chosen.</summary>
    public string? HgdgChecklist { get; set; }

    /// <summary>0–20, one decimal. Null = not scored, so the GAD figures print blank (decision 9).</summary>
    public decimal? HgdgScore { get; set; }

    // ── Narrative sections (sanitized HTML, decision 20) ─────────────────────

    /// <summary>Section B. Pre-filled from <c>AipProject.Description</c> once, at creation.</summary>
    public string? Description { get; set; }

    /// <summary>Section C.</summary>
    public string? Rationale { get; set; }

    /// <summary>Section E. Pre-filled from <c>AipProject.Objective</c> once, at creation.</summary>
    public string? GeneralObjective { get; set; }

    /// <summary>Section J.</summary>
    public string? PartnershipSustainability { get; set; }

    /// <summary>Plain text, ≤ 4,000.</summary>
    public string? WomensImpactStrategy { get; set; }

    // ── Section I ─────────────────────────────────────────────────────────────

    public string? ProjectSupervisor { get; set; }
    public string? ProjectManager { get; set; }

    /// <summary>
    /// While true, Section F's Direct beneficiaries are Section A's rows and this proposal stores no
    /// <c>Direct</c> rows of its own (decision 10). Defaults to true.
    /// </summary>
    public bool DirectSameAsSummary { get; set; } = true;

    // ── Signatories: always exactly three slots, stored flat (decision 19) ───

    public string? Signatory1Label { get; set; }
    public string? Signatory1Name { get; set; }
    public string? Signatory1Position { get; set; }
    public string? Signatory2Label { get; set; }
    public string? Signatory2Name { get; set; }
    public string? Signatory2Position { get; set; }
    public string? Signatory3Label { get; set; }
    public string? Signatory3Name { get; set; }
    public string? Signatory3Position { get; set; }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The AIP-sourced <c>header</c> and <c>aipRows</c>, serialized on Finalize with a
    /// <c>schemaVersion</c>. Null while Draft. Readers must accept every version ever written.
    /// </summary>
    public string? SnapshotJson { get; set; }

    /// <summary>UTC. Null while Draft.</summary>
    public DateTime? FinalizedAt { get; set; }
    public Guid? FinalizedById { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }
    public Guid? CreatedById { get; set; }

    /// <summary>
    /// UTC. ⚠️ Every write must set this (and <see cref="UpdatedById"/>), even one that only
    /// changes child rows: the parent row has to be updated for <see cref="RowVersion"/> to be
    /// checked and bumped. A save that touches children only would skip the concurrency check.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
    public Guid? UpdatedById { get; set; }

    /// <summary>
    /// SQL Server <c>rowversion</c> (decision 23). The whole proposal is one unit of conflict:
    /// a write that carries a stale value is rejected and writes nothing.
    /// </summary>
    public byte[] RowVersion { get; set; } = [];

    // ── Navigation ────────────────────────────────────────────────────────────

    public AipProject? AipProject { get; set; }
    public User? FinalizedBy { get; set; }
    public User? CreatedBy { get; set; }
    public User? UpdatedBy { get; set; }

    public ICollection<InvestmentProposalBeneficiary> Beneficiaries { get; set; } = new List<InvestmentProposalBeneficiary>();
    public ICollection<InvestmentProposalBenefit> Benefits { get; set; } = new List<InvestmentProposalBenefit>();
    public ICollection<InvestmentProposalLogframe> Logframe { get; set; } = new List<InvestmentProposalLogframe>();
    public ICollection<InvestmentProposalGroup> Groups { get; set; } = new List<InvestmentProposalGroup>();
    public ICollection<InvestmentProposalWorkPlanRow> WorkPlanRows { get; set; } = new List<InvestmentProposalWorkPlanRow>();
    public ICollection<InvestmentProposalTeamMember> TeamMembers { get; set; } = new List<InvestmentProposalTeamMember>();
    public ICollection<InvestmentProposalCapacityTraining> CapacityTrainings { get; set; } = new List<InvestmentProposalCapacityTraining>();
    public ICollection<InvestmentProposalMonitoring> Monitoring { get; set; } = new List<InvestmentProposalMonitoring>();
    public ICollection<InvestmentProposalRisk> Risks { get; set; } = new List<InvestmentProposalRisk>();
}
