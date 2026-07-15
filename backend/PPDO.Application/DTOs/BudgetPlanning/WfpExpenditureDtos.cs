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
/// Ceiling status for one (AIP activity, division, fiscal year) — §8/RAL-122. Amounts are in
/// PESOS: AipBudget is the AIP activity's Total × 1000, converted here (the one place this
/// conversion happens) so the frontend never has to. AipBudget/AipUsed stay aggregate across
/// ALL funding sources (v1.4.3 §2 D3 — the AIP total has no per-fund breakdown to split
/// against). DivisionAllocation/DivisionRemaining are General Fund's specifically (v1.4.3 —
/// RAL-154), kept at the top level so existing callers built before the fund-source dimension
/// keep working unchanged. <see cref="Funds"/> carries every active fund source's allocation/
/// remaining (v1.4.3 — RAL-154) for callers that render per-fund bars (RAL-156).
/// </summary>
public record WfpCeilingStatusDto(
    decimal AipBudget,
    decimal AipUsed,
    decimal DivisionAllocation,
    decimal DivisionRemaining,
    IReadOnlyList<WfpFundCeilingDto> Funds);

/// <summary>One funding source's division-allocation status (v1.4.3 — RAL-154).</summary>
public record WfpFundCeilingDto(
    int     FundingSourceId,
    string  FundingSourceCode,
    string  FundingSourceName,
    decimal Allocation,
    decimal Remaining);
