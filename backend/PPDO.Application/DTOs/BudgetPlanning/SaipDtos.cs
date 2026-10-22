namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One SAIP, as the history list and the SAIP banner show it (v1.10.0 — PPDO-246, spec §4).
/// Slim on purpose: no tree, no diff — those have their own endpoints (T2/T3).
/// </summary>
/// <param name="DraftAipOfficeIds">
/// The draft copy's group rows while the SAIP is open; empty once it is Approved or Cancelled.
/// </param>
public sealed record SaipDto(
    int       Id,
    int       AipRecordId,
    int       FiscalYear,
    int       OfficeId,
    int       Number,
    string    Status,
    bool      IsOpen,
    DateTime  StartedAt,
    Guid      StartedById,
    string?   StartedByName,
    DateTime? SubmittedAt,
    DateTime? DecidedAt,
    DateTime? CancelledAt,
    string?   CancelledByName,
    string?   LetterFileName,
    IReadOnlyList<int> DraftAipOfficeIds);

/// <summary>What a change did to its row (spec §5 <c>aip_saip_changes.change</c>).</summary>
public static class SaipChangeKind
{
    public const string Added         = "Added";
    public const string Changed       = "Changed";
    public const string ReducedToZero = "ReducedToZero";

    /// <summary>
    /// An approved row with no copy. ⚠️ Unreachable through the endpoints — the delete rule refuses it
    /// (decision 5) — and reported rather than hidden if it ever happens, so a lost row cannot read
    /// as "no changes".
    /// </summary>
    public const string Removed       = "Removed";
}

/// <summary>Which level of the tree a change is on (spec §5 <c>aip_saip_changes.node_type</c>).</summary>
public static class SaipNodeType
{
    public const string Program     = "Program";
    public const string Project     = "Project";
    public const string Activity    = "Activity";
    public const string Expenditure = "Expenditure";
}

/// <summary>
/// One change an SAIP makes (v1.10.0 — PPDO-247, decision 17). An added row is one entry with no
/// field; a changed row is one entry per changed field.
/// </summary>
/// <param name="NodeId">The copy's row id while the SAIP is open (the approved id once T3 saves it).</param>
/// <param name="SourceId">The approved row it came from; null when added in the SAIP.</param>
/// <param name="ActivityId">
/// The activity (PPA) the change belongs to — the row itself for an activity, its parent for a line;
/// null for a program or project. What the "changes so far" panel groups by.
/// </param>
/// <param name="Before">The approved value as text; null for an added row.</param>
/// <param name="After">The amended value as text; for an added activity or line, its total.</param>
public sealed record SaipChangeDto(
    string  NodeType,
    int     NodeId,
    int?    SourceId,
    int?    ActivityId,
    string  RefCode,
    string  Name,
    string  Change,
    string? Field,
    string? Before,
    string? After);

/// <summary>Approved and amended totals for one fund (null fund: lines naming no fund).</summary>
public sealed record SaipFundNetDto(
    int?    FundingSourceId,
    string? FundCode,
    decimal Approved,
    decimal Amended,
    decimal Difference);

/// <summary>
/// The "changes so far" view of one SAIP (spec §4 <c>GET …/saips/{id}/changes</c>, §6 panel).
/// <paramref name="IsLive"/> is true while it is computed from the open copy.
/// </summary>
/// <param name="CeilingAfter">
/// The office's ceiling status measured on the amended totals (decision 10); null when there is no
/// copy to measure.
/// </param>
public sealed record SaipChangesDto(
    int     SaipId,
    int     Number,
    string  Status,
    bool    IsLive,
    IReadOnlyList<SaipChangeDto>  Changes,
    IReadOnlyList<SaipFundNetDto> NetByFund,
    decimal ApprovedTotal,
    decimal AmendedTotal,
    decimal NetChange,
    AipCeilingStatusDto? CeilingAfter);

/// <summary>An SAIP's request letter, read back for download (PPDO-247).</summary>
public sealed record SaipLetterFile(byte[] Content, string FileName, string ContentType);
