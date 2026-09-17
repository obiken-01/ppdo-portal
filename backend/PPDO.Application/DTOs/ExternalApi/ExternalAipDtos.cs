namespace PPDO.Application.DTOs.ExternalApi;

/// <summary>
/// The payload shapes for <c>GET /api/external/v1/aip</c> (v1.8.0 — PPDO-14), matching
/// <c>docs/external-api/aip-response.schema.json</c> (draft 1.0.0) field for field. Every money
/// value is a decimal string, never a JSON number — see <see cref="ExternalMoneyAmountsDto"/>.
/// </summary>

/// <summary>PS / MOOE / CO and their total, each a decimal string with exactly two places.</summary>
public sealed record ExternalMoneyAmountsDto(string Ps, string Mooe, string Co, string Total);

/// <summary>A funding source as recorded at entry time — a snapshot, per build spec §2 decision 8.</summary>
public sealed record ExternalFundingSourceDto(string Code, string? Name);

public sealed record ExternalFundTotalDto(ExternalFundingSourceDto FundingSource, ExternalMoneyAmountsDto Amounts);

/// <summary>The office the <c>officeCode</c> query parameter addresses.</summary>
public sealed record ExternalOfficeRefDto(string Code, string Name);

/// <summary>One of the four fixed AIP sectors — code and name always travel together.</summary>
public sealed record ExternalSectorDto(string Code, string Name);

/// <summary>Climate Change Expenditure Tagging — base pesos, the +30% uplift never applies here.</summary>
public sealed record ExternalClimateChangeDto(
    string Adaptation, string Mitigation, IReadOnlyList<string> TypologyCodes);

public sealed record ExternalAccountDto(string Number, string Title, string ExpenseClass);

/// <summary>FY2028+ only. One item an expenditure line is itemised into.</summary>
public sealed record ExternalProcurementItemDto(
    string Name,
    string Unit,
    string UnitPrice,
    string Qty,
    string NumberOfDays,
    string LineTotal,
    string? StockCardNo,
    bool IsFromPriceIndex);

/// <summary>FY2028+ only. One expenditure line under an activity.</summary>
public sealed record ExternalExpenditureDto(
    ExternalAccountDto Account,
    ExternalFundingSourceDto FundingSource,
    ExternalMoneyAmountsDto Amounts,
    IReadOnlyList<ExternalProcurementItemDto> ProcurementItems);

public sealed record ExternalScheduleDto(string? Start, string? End);

/// <summary>Leaf of the PPA tree — all money lives here.</summary>
public sealed record ExternalActivityDto(
    string RefCode,
    string Name,
    bool IsSynthetic,
    string? EsreCode,
    string? ImplementingOffice,
    ExternalScheduleDto Schedule,
    string? ExpectedOutputs,
    ExternalFundingSourceDto? FundingSource,
    ExternalMoneyAmountsDto Amounts,
    ExternalMoneyAmountsDto? PrintedAmounts,
    ExternalClimateChangeDto ClimateChange,
    IReadOnlyList<ExternalExpenditureDto> Expenditures);

public sealed record ExternalProjectDto(
    string RefCode,
    string Name,
    bool IsSynthetic,
    ExternalMoneyAmountsDto Totals,
    ExternalMoneyAmountsDto? PrintedTotals,
    IReadOnlyList<ExternalActivityDto> Activities);

public sealed record ExternalProgramDto(
    string RefCode,
    string Name,
    string? FunctionBand,
    ExternalMoneyAmountsDto Totals,
    ExternalMoneyAmountsDto? PrintedTotals,
    IReadOnlyList<ExternalProjectDto> Projects);

/// <summary>One office-level row of the AIP (Annex B column B) — identified by (sector, name), not refCode alone.</summary>
public sealed record ExternalGroupDto(
    ExternalSectorDto Sector,
    string RefCode,
    string Name,
    ExternalMoneyAmountsDto Totals,
    ExternalMoneyAmountsDto? PrintedTotals,
    IReadOnlyList<ExternalProgramDto> Programs);

/// <summary>One released office's AIP for the fiscal year.</summary>
public sealed record ExternalOfficeAipDto(
    ExternalOfficeRefDto Office,
    string Status,
    string? ReleasedAt,
    ExternalMoneyAmountsDto Totals,
    ExternalMoneyAmountsDto? PrintedTotals,
    IReadOnlyList<ExternalFundTotalDto> TotalsByFundingSource,
    IReadOnlyList<ExternalGroupDto> Groups);

/// <summary>The whole response for one fiscal year — every released office, plus the offices still pending.</summary>
public sealed record ExternalAipDto(
    string SchemaVersion,
    string GeneratedAt,
    int FiscalYear,
    string AipFormat,
    string Currency,
    string? OfficeCode,
    ExternalMoneyAmountsDto Totals,
    ExternalMoneyAmountsDto? PrintedTotals,
    IReadOnlyList<ExternalFundTotalDto> TotalsByFundingSource,
    IReadOnlyList<ExternalOfficeAipDto> Offices,
    IReadOnlyList<ExternalOfficeRefDto> PendingOffices);

/// <summary>Fixed values the schema pins — kept in one place so nothing hand-types them twice.</summary>
public static class ExternalAipConstants
{
    public const string SchemaVersion = "1.1.0";
    public const string Currency = "PHP";

    public const string FormatLegacy = "Legacy";
    public const string FormatFy2028 = "Fy2028";

    public const string StatusFinal = "Final";
    public const string StatusConsolidated = "Consolidated";
}
