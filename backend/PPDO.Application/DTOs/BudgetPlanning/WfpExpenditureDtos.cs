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
    decimal  NumberOfDays,
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
    decimal  Qty,
    decimal  NumberOfDays = 1m);

/// <summary>
/// Create/update body for a WFP expenditure. Id null = create; Id provided = replace an
/// existing expenditure's periods/procurement items in place (delete-then-reinsert, matching
/// the LdipService/WfpService convention). No Q1–Q4/Net/Total fields — those are always
/// computed server-side by <c>WfpExpenditureCalculator</c>, never accepted from the client.
///
/// ReserveAmount is nullable (RAL-121): null means "not specified" — the service defaults it
/// to the reserve rate × the expenditure's Net when ApplyReserve is true. An explicit value
/// (including 0) is respected as-is (still capped at the rate × Net ceiling), never silently
/// overridden by the default.
/// </summary>
public record SaveWfpExpenditureDto(
    int?     Id,
    int      WfpActivityId,
    int?     AccountId,
    string   Nature,
    string   Frequency,
    int?     FundingSourceId,
    bool     ApplyReserve,
    decimal? ReserveAmount,
    int?     AnnualQuarterChoice,
    IReadOnlyList<SaveWfpExpenditurePeriodDto>   Periods,
    IReadOnlyList<SaveWfpProcurementItemDto>     ProcurementItems);

/// <summary>The current reserve rate, surfaced to the frontend so it never hard-codes "10%" (RAL-121).</summary>
public record WfpReserveRateDto(decimal Rate);

/// <summary>
/// Ceiling status for one (AIP activity, division, fiscal year) — §8/RAL-122. Both amounts
/// are in PESOS: AipBudget is the AIP activity's Total × 1000, converted here (the one place
/// this conversion happens) so the frontend never has to. Consumed by ticket #9's live UI
/// check and the same computation is reused server-side for the block-on-save/finalize checks.
/// </summary>
public record WfpCeilingStatusDto(
    decimal AipBudget,
    decimal AipUsed,
    decimal DivisionAllocation,
    decimal DivisionRemaining);
