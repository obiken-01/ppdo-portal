namespace PPDO.Application.DTOs.InvestmentProposal;

// Investment proposal DTOs (v1.8.0 Demo 2.15 — PPDO-155). Shapes are spec §4, revised by
// PPDO-173: four signatory slots, training on the team member, fund names on the AIP rows.
// Amounts are full pesos, never rounded and never +30% (decision 7).

// ── Lists ─────────────────────────────────────────────────────────────────────

/// <summary>One row of <c>GET /budget-planning/proposals</c>: an AIP project and its proposal status.</summary>
public sealed record ProposalListItemDto(
    int       AipProjectId,
    string    ProjectRefCode,
    string    ProjectName,
    string    ProgramName,
    string    OfficeName,
    decimal   ProjectCost,
    int?      ProposalId,
    string    Status,
    DateTime? UpdatedAt,
    string?   UpdatedByName);

/// <summary>
/// PPDO-180 — projects by proposal status, for the dashboard: a <c>Final</c> proposal, a
/// <c>Draft</c>, or none yet. Their sum is the projects in scope.
/// </summary>
public sealed record ProposalCountsDto(int Final, int Draft, int None);

/// <summary>PPDO-180 — a project still needing a proposal. <c>Status</c> is <c>None</c> or <c>Draft</c>.</summary>
public sealed record ProposalAttentionDto(
    int     AipProjectId,
    string  ProjectRefCode,
    string  ProjectName,
    int?    ProposalId,
    string  Status);

/// <summary>
/// PPDO-180 — the dashboard's Investment proposals band for one office: the counts, and the first
/// few projects that still need one (none first, then drafts).
/// </summary>
public sealed record OfficeProposalSummaryDto(
    ProposalCountsDto                   Counts,
    IReadOnlyList<ProposalAttentionDto> NeedsAttention);

/// <summary>A page of <see cref="ProposalListItemDto"/>. <see cref="TotalCount"/> counts the whole match.</summary>
public sealed record ProposalListPageDto(
    IReadOnlyList<ProposalListItemDto> Items, int TotalCount, int Page, int PageSize);

/// <summary>One option of the editor's project picker and AIP Entry's strip (<c>GET /proposals/projects</c>).</summary>
public sealed record ProposalProjectOptionDto(
    int       AipProjectId,
    string    ProjectRefCode,
    string    ProjectName,
    int       ProgramId,
    string    ProgramRefCode,
    string    ProgramName,
    int?      ProposalId,
    string    Status,
    DateTime? UpdatedAt,
    string?   UpdatedByName);

// ── The proposal ──────────────────────────────────────────────────────────────

/// <summary>
/// <c>GET /proposals/{id}</c> and every write's response. <see cref="RowVersion"/> is base64 and
/// goes back unchanged on the next write. <see cref="CanEdit"/> and <see cref="CanReopen"/> are
/// what the caller may do now, so the editor hides what would answer 403.
/// </summary>
public sealed record ProposalDto(
    int                  Id,
    string               Status,
    string               RowVersion,
    DateTime?            FinalizedAt,
    string?              FinalizedByName,
    DateTime             UpdatedAt,
    string?              UpdatedByName,
    bool                 AipChangedSinceFinal,
    bool                 CanEdit,
    bool                 CanReopen,
    ProposalHeaderDto    Header,
    ProposalWarningsDto  Warnings,
    ProposalContentDto   Content,
    IReadOnlyList<ProposalAipRowDto> AipRows);

/// <summary>
/// Section A's AIP-sourced values (decision 6). Snapshotted on Finalize. The ids are for the
/// editor's picker and its Open in AIP Entry link.
/// </summary>
public sealed record ProposalHeaderDto(
    int                   AipProjectId,
    int                   ProgramId,
    int                   AipOfficeId,
    int?                  OfficeId,
    int                   FiscalYear,
    string                ProgramTitle,
    string                ProjectRefCode,
    string                ProjectTitle,
    string                Proponent,
    string?               ScheduleStart,
    string?               ScheduleEnd,
    decimal               ProjectCost,
    IReadOnlyList<string> FundingSources,
    string                ClimateTypology,
    decimal?              AttributedGadBudget);

/// <summary>Activities that print without a breakdown (decision 18).</summary>
public sealed record ProposalWarningsDto(IReadOnlyList<ProposalActivityRefDto> ActivitiesWithoutLines);

public sealed record ProposalActivityRefDto(int ActivityId, string RefCode, string Name);

/// <summary>One AIP activity for G and H-1 (decisions 16–18).</summary>
public sealed record ProposalAipRowDto(
    int                   ActivityId,
    string                RefCode,
    string                Name,
    string?               Timeline,
    string?               Opr,
    decimal               Ps,
    decimal               Mooe,
    decimal               Co,
    decimal               Total,
    IReadOnlyList<string> FundNames,
    IReadOnlyList<ProposalExpenditureDto> Expenditures);

public sealed record ProposalExpenditureDto(
    string?  AccountCode,
    string?  AccountTitle,
    decimal  Ps,
    decimal  Mooe,
    decimal  Co,
    decimal  Total,
    string?  FundName,
    IReadOnlyList<ProposalItemDto> Items);

public sealed record ProposalItemDto(
    string  Name,
    decimal UnitPrice,
    decimal Qty,
    string  Unit,
    decimal NumberOfDays,
    decimal LineTotal);

// ── What the user edits (the PUT body's content) ──────────────────────────────

/// <summary>
/// Everything the user types. A PUT replaces the child collections whole: a row missing from the
/// body is deleted. Rich-text fields are HTML, sanitized on save to the decision-20 allow-list.
/// </summary>
public sealed record ProposalContentDto(
    string?                                  ProjectLocation,
    string?                                  HgdgChecklist,
    decimal?                                 HgdgScore,
    IReadOnlyList<ProposalBeneficiaryDto>     BeneficiariesSummary,
    string?                                  Description,
    string?                                  Rationale,
    IReadOnlyList<ProposalBenefitDto>         Benefits,
    string?                                  GeneralObjective,
    IReadOnlyList<ProposalLogframeDto>        Logframe,
    bool                                     DirectSameAsSummary,
    IReadOnlyList<ProposalTargetBeneficiaryDto> TargetBeneficiaries,
    IReadOnlyList<ProposalGroupDto>           Groups,
    IReadOnlyList<ProposalWorkPlanRowDto>     WorkPlan,
    string?                                  ProjectSupervisor,
    string?                                  ProjectManager,
    IReadOnlyList<ProposalTeamMemberDto>      TeamMembers,
    string?                                  PartnershipSustainability,
    IReadOnlyList<ProposalMonitoringDto>      Monitoring,
    IReadOnlyList<ProposalRiskDto>            Risks,
    string?                                  WomensImpactStrategy,
    IReadOnlyList<ProposalSignatoryDto>       Signatories);

public sealed record ProposalBeneficiaryDto(int? Id, string? Indicator, int? Male, int? Female);

/// <summary><see cref="Kind"/> is <c>Direct</c> or <c>Indirect</c>. Direct rows are ignored while DirectSameAsSummary is on.</summary>
public sealed record ProposalTargetBeneficiaryDto(int? Id, string? Kind, string? Name, int? Male, int? Female);

public sealed record ProposalBenefitDto(string? Sector, string? Benefit, string? Cost);

/// <summary><see cref="Level"/> is the stored code: Impact, Outcome, Output, Input.</summary>
public sealed record ProposalLogframeDto(string? Level, string? Target, string? Verification);

/// <summary>
/// A work-plan group. <see cref="ClientKey"/> is how rows point at it in the same body (a new
/// group has no id yet). Responses set it to <c>g{id}</c>. Order is array order.
/// </summary>
public sealed record ProposalGroupDto(int? Id, string? ClientKey, string? Label);

/// <summary>
/// One Section G row. An AIP row has <see cref="AipActivityId"/>; a proposal-only step has
/// <see cref="Name"/>, <see cref="Timeline"/> and <see cref="Opr"/> instead. Order is array order.
/// </summary>
public sealed record ProposalWorkPlanRowDto(
    int?    Id,
    int?    AipActivityId,
    string? Name,
    string? GroupKey,
    string? PerformanceTarget,
    string? GenderIssues,
    string? Timeline,
    string? Opr);

/// <summary><see cref="Sex"/> is <c>M</c> or <c>F</c>. <see cref="RequiredTraining"/> is decision 31.</summary>
public sealed record ProposalTeamMemberDto(
    int? Id, string? Name, string? Sex, string? GadTrainings, string? Expertise, string? RequiredTraining);

/// <summary><see cref="Phase"/> is <c>Pre</c>, <c>During</c> or <c>Post</c>.</summary>
public sealed record ProposalMonitoringDto(int? Id, string? Phase, string? Activity, string? Schedule, string? Tools);

public sealed record ProposalRiskDto(int? Id, string? Risk, string? Prevention, string? Monitoring);

/// <summary>Slots 1–4 (decision 19). A slot with no name is not printed.</summary>
public sealed record ProposalSignatoryDto(int Slot, string? Label, string? Name, string? Position);

// ── Request bodies ────────────────────────────────────────────────────────────

public sealed record CreateProposalDto(int AipProjectId);

/// <summary>PUT body: the version the client loaded, and the whole editable content.</summary>
public sealed record UpdateProposalDto(string? RowVersion, ProposalContentDto? Content);

/// <summary>Finalize and reopen: just the version the client loaded.</summary>
public sealed record RowVersionDto(string? RowVersion);

// ── Error data ────────────────────────────────────────────────────────────────

/// <summary>The data of a stale-version 409 (spec §4 common errors).</summary>
public sealed record ProposalConflictDto(string CurrentRowVersion, string? UpdatedByName, DateTime UpdatedAt);

/// <summary>The data of a duplicate-create 409: where the existing proposal is.</summary>
public sealed record ProposalExistsDto(int ProposalId);

/// <summary>The data of a validation 400: messages keyed by field path, e.g. <c>teamMembers[2].sex</c>.</summary>
public sealed record ProposalValidationErrorsDto(IReadOnlyDictionary<string, IReadOnlyList<string>> Errors);

// ── Export ────────────────────────────────────────────────────────────────────

/// <summary>The Word file of <c>GET /proposals/{id}/export</c> (PPDO-158).</summary>
public sealed record ProposalExportFileDto(string FileName, byte[] Content);
