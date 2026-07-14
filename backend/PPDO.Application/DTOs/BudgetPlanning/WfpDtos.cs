namespace PPDO.Application.DTOs.BudgetPlanning;

// ── Read DTOs ─────────────────────────────────────────────────────────────────

public record WfpExpenditureLineDto(
    int      Id,
    int      WfpActivityId,
    string   ExpenditureType,
    string?  ResourcesNeeded,
    string?  ResponsibleUnit,
    string?  SuccessIndicator,
    string?  MeansOfVerification,
    int?     AccountId,
    string?  AccountNumberSnapshot,
    string?  AccountTitleSnapshot,
    decimal? TotalAppropriation,
    bool     ApplyReserve,
    decimal? ReserveAmount,
    decimal? NetAppropriation,
    decimal? Q1,
    decimal? Q2,
    decimal? Q3,
    decimal? Q4,
    decimal? QuarterlyTotal,
    int?     FundingSourceId,
    string?  FundingSourceSnapshot,
    string?  FundingSourceNameSnapshot,
    int      SortOrder);

public record WfpActivityDto(
    int    Id,
    int    WfpId,
    int    AipActivityId,
    IReadOnlyList<WfpExpenditureLineDto> Lines);

public record WfpRecordDto(
    int       Id,
    int       AipRecordId,
    int       OfficeId,
    int?      DivisionId,
    int       FiscalYear,
    string    Status,
    Guid      CreatedById,
    DateTime  CreatedAt,
    DateTime  UpdatedAt,
    DateTime? FinalizedAt,
    int?      SourceId);

public record WfpRecordDetailDto(
    int       Id,
    int       AipRecordId,
    int       OfficeId,
    int?      DivisionId,
    int       FiscalYear,
    string    Status,
    Guid      CreatedById,
    DateTime  CreatedAt,
    DateTime  UpdatedAt,
    DateTime? FinalizedAt,
    int?      SourceId,
    IReadOnlyList<WfpActivityDto> Activities);

// ── Save DTOs (client → server) ───────────────────────────────────────────────

public record SaveWfpExpenditureLineDto(
    string   ExpenditureType,
    string?  ResourcesNeeded,
    string?  ResponsibleUnit,
    string?  SuccessIndicator,
    string?  MeansOfVerification,
    int?     AccountId,
    decimal? TotalAppropriation,
    bool     ApplyReserve,
    decimal? Q1,
    decimal? Q2,
    decimal? Q3,
    decimal? Q4,
    int?     FundingSourceId,
    int      SortOrder);

public record SaveWfpActivityDto(
    int AipActivityId,
    IReadOnlyList<SaveWfpExpenditureLineDto> Lines);

public record SaveWfpDto(
    int AipRecordId,
    int OfficeId,
    int FiscalYear,
    int? DivisionId,
    IReadOnlyList<SaveWfpActivityDto> Activities);

// ── v1.4 entry wizard enabler (RAL-123) ───────────────────────────────────────

/// <summary>Request body for find-or-create WFP record/activity — see <c>IWfpService.EnsureActivityAsync</c>.</summary>
public record EnsureWfpActivityDto(
    int AipRecordId,
    int OfficeId,
    int? DivisionId,
    int FiscalYear,
    int AipActivityId);

/// <summary>The (wfp_record_id, wfp_activity_id) pair the v1.4 entry wizard needs to call RAL-120's expenditure save endpoint.</summary>
public record WfpActivityRefDto(
    int WfpRecordId,
    int WfpActivityId,
    string WfpStatus);

/// <summary>
/// Result of a scoped WFP cleanup (RAL-137, live-testing reset tool). Row counts are captured
/// before deletion — the actual removal relies on DB cascade (WfpActivity → WfpExpenditure →
/// WfpExpenditurePeriod/WfpProcurementItem, plus WfpDivisionAllocationLedger, all cascade off
/// WfpRecord).
/// </summary>
public record WfpCleanupResultDto(
    int WfpRecordId,
    int OfficeId,
    int? DivisionId,
    int FiscalYear,
    bool WasFinal,
    int ActivitiesDeleted,
    int ExpendituresDeleted,
    int LegacyLinesDeleted);
