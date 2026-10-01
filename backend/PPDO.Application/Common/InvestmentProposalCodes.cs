namespace PPDO.Application.Common;

/// <summary>
/// The fixed string values stored in the investment proposal tables (v1.8.0 Demo 2.15 —
/// PPDO-154, <c>docs/v1.8/Investment_Proposal_Spec.md</c> §5). Each column's CHECK constraint is
/// built from the matching <c>All</c> list, so the database and these constants cannot drift.
///
/// </summary>
public static class InvestmentProposalStatus
{
    /// <summary>Editable. AIP values are read live.</summary>
    public const string Draft = "Draft";

    /// <summary>Locked, with the AIP values snapshotted (decision 4).</summary>
    public const string Final = "Final";

    public static readonly IReadOnlyList<string> All = [Draft, Final];
}

/// <summary>
/// Section D sectors, in print order — the template's five rows, word for word (PPDO-173).
/// </summary>
public static class InvestmentProposalSector
{
    public const string Social         = "Social";
    public const string Economic       = "Economic";
    public const string Environmental  = "Environmental";
    public const string Institutional  = "Institutional";
    public const string Infrastructure = "Infrastructure/Land Use";

    public static readonly IReadOnlyList<string> All = [Social, Economic, Environmental, Institutional, Infrastructure];
}

/// <summary>Which table a beneficiary row belongs to (decision 10).</summary>
public static class InvestmentProposalBeneficiarySection
{
    /// <summary>Section A's disaggregated table.</summary>
    public const string Summary = "Summary";

    /// <summary>Section F, direct. Only stored while "Same as Section A" is off.</summary>
    public const string Direct = "Direct";

    /// <summary>Section F, indirect.</summary>
    public const string Indirect = "Indirect";

    public static readonly IReadOnlyList<string> All = [Summary, Direct, Indirect];
}

/// <summary>Section E logframe levels, in print order.</summary>
public static class InvestmentProposalLogframeLevel
{
    public const string Impact  = "Impact";
    public const string Outcome = "Outcome";
    public const string Output  = "Output";
    public const string Input   = "Input";

    public static readonly IReadOnlyList<string> All = [Impact, Outcome, Output, Input];
}

/// <summary>Section K monitoring phases, in print order.</summary>
public static class InvestmentProposalMonitoringPhase
{
    public const string Pre    = "Pre";
    public const string During = "During";
    public const string Post   = "Post";

    public static readonly IReadOnlyList<string> All = [Pre, During, Post];
}

/// <summary>Section I team member sex.</summary>
public static class InvestmentProposalSex
{
    public const string Male   = "M";
    public const string Female = "F";

    public static readonly IReadOnlyList<string> All = [Male, Female];
}
