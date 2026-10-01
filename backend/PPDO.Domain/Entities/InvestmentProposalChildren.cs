namespace PPDO.Domain.Entities;

// The eight child tables of an investment proposal (PPDO-154, spec §5 "Child tables"). They are
// small, belong to one proposal each, cascade from it, and are replaced whole on every save, so
// they share one file rather than nine near-empty ones.

/// <summary>
/// A disaggregated beneficiary row. <see cref="Section"/> is <c>Summary</c> (Section A),
/// <c>Direct</c> or <c>Indirect</c> (Section F). Counts may be blank: label-only rows exist in
/// the province's samples. Total = Male + Female is computed, never stored.
/// </summary>
public sealed class InvestmentProposalBeneficiary
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string Section { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int? Male { get; set; }
    public int? Female { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Section D: one row per sector, unique per proposal.</summary>
public sealed class InvestmentProposalBenefit
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string Sector { get; set; } = string.Empty;
    public string? Benefit { get; set; }
    public string? Cost { get; set; }
}

/// <summary>Section E: one row per logframe level, unique per proposal.</summary>
public sealed class InvestmentProposalLogframe
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string Level { get; set; } = string.Empty;
    public string? Target { get; set; }
    public string? Verification { get; set; }
}

/// <summary>
/// A proposal-only component group for the work plan (decision 14). One grouping drives both
/// Section G and the H-1 annex.
/// </summary>
public sealed class InvestmentProposalGroup
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string Label { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}

/// <summary>
/// One Section G row. Either an AIP activity (<see cref="AipActivityId"/> set; timeline and OPR
/// come from the AIP) or a proposal-only step (<see cref="Name"/> set; never carries money).
/// A CHECK constraint requires one of the two.
/// </summary>
public sealed class InvestmentProposalWorkPlanRow
{
    public int Id { get; set; }
    public int ProposalId { get; set; }

    /// <summary>FK to the AIP activity. Cascades: an activity deleted from the AIP takes its typed G text with it (decision 16).</summary>
    public int? AipActivityId { get; set; }

    /// <summary>
    /// FK to the group. <c>NO ACTION</c>, not cascade (the multiple-cascade-path rule), so clear it
    /// before deleting a group. The replace-all save does that naturally.
    /// </summary>
    public int? GroupId { get; set; }

    /// <summary>Proposal-only rows only.</summary>
    public string? Name { get; set; }
    public string? PerformanceTarget { get; set; }
    public string? GenderIssues { get; set; }

    /// <summary>Proposal-only rows only. AIP rows read it from the activity.</summary>
    public string? Timeline { get; set; }

    /// <summary>Proposal-only rows only. AIP rows read it from <c>ImplementingOffice</c>.</summary>
    public string? Opr { get; set; }

    public int SortOrder { get; set; }

    public AipActivity? AipActivity { get; set; }
    public InvestmentProposalGroup? Group { get; set; }
}

/// <summary>
/// Section I team member. <see cref="RequiredTraining"/> is the member's row in the template's
/// "Required Capacity Development Training" table, which lists every member. The export merges
/// identical neighbouring cells, as the province's samples do.
/// </summary>
public sealed class InvestmentProposalTeamMember
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary><c>M</c> or <c>F</c>.</summary>
    public string Sex { get; set; } = string.Empty;
    public string? GadTrainings { get; set; }
    public string? Expertise { get; set; }
    public string? RequiredTraining { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Section K monitoring row. <see cref="Phase"/> is <c>Pre</c>, <c>During</c> or <c>Post</c>.</summary>
public sealed class InvestmentProposalMonitoring
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string Phase { get; set; } = string.Empty;
    public string Activity { get; set; } = string.Empty;
    public string? Schedule { get; set; }
    public string? Tools { get; set; }
    public int SortOrder { get; set; }
}

/// <summary>Section L risk row.</summary>
public sealed class InvestmentProposalRisk
{
    public int Id { get; set; }
    public int ProposalId { get; set; }
    public string Risk { get; set; } = string.Empty;
    public string? Prevention { get; set; }
    public string? Monitoring { get; set; }
    public int SortOrder { get; set; }
}
