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
    IReadOnlyList<SaveWfpActivityDto> Activities);
