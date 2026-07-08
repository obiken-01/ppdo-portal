namespace PPDO.Application.DTOs.BudgetPlanning;

// ── Read DTOs ─────────────────────────────────────────────────────────────────

public record WfpExpenditurePeriodDto(int PeriodNo, decimal Amount);

public record WfpProcurementItemDto(
    int      PeriodNo,
    int?     PriceIndexItemId,
    string   Name,
    string   Unit,
    decimal  UnitPrice,
    decimal  Qty,
    decimal  LineTotal);

/// <summary>Read model for a WFP expenditure — Q1–Q4/Net/Total are always server-computed (RAL-120).</summary>
public record WfpExpenditureDto(
    int      Id,
    int      WfpActivityId,
    int?     AccountId,
    string?  AccountNumberSnapshot,
    string?  AccountTitleSnapshot,
    string   Nature,
    string   Frequency,
    int?     FundingSourceId,
    string?  FundingSourceSnapshot,
    string?  FundingSourceNameSnapshot,
    bool     ApplyReserve,
    decimal  ReserveAmount,
    int?     AnnualQuarterChoice,
    decimal  Q1,
    decimal  Q2,
    decimal  Q3,
    decimal  Q4,
    decimal  NetAppropriation,
    decimal  TotalAppropriation,
    IReadOnlyList<WfpExpenditurePeriodDto>   Periods,
    IReadOnlyList<WfpProcurementItemDto>     ProcurementItems);

// ── Save DTOs (client → server) ───────────────────────────────────────────────

public record SaveWfpExpenditurePeriodDto(int PeriodNo, decimal Amount);

public record SaveWfpProcurementItemDto(
    int      PeriodNo,
    int?     PriceIndexItemId,
    string   Name,
    string   Unit,
    decimal  UnitPrice,
    decimal  Qty);

/// <summary>
/// Create/update body for a WFP expenditure. Id null = create; Id provided = replace an
/// existing expenditure's periods/procurement items in place (delete-then-reinsert, matching
/// the LdipService/WfpService convention). No Q1–Q4/Net/Total fields — those are always
/// computed server-side by <c>WfpExpenditureCalculator</c>, never accepted from the client.
/// </summary>
public record SaveWfpExpenditureDto(
    int?     Id,
    int      WfpActivityId,
    int?     AccountId,
    string   Nature,
    string   Frequency,
    int?     FundingSourceId,
    bool     ApplyReserve,
    decimal  ReserveAmount,
    int?     AnnualQuarterChoice,
    IReadOnlyList<SaveWfpExpenditurePeriodDto>   Periods,
    IReadOnlyList<SaveWfpProcurementItemDto>     ProcurementItems);
