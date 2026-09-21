using System.Globalization;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.DTOs.ExternalApi;
using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Pure entity-to-external-DTO mapping for the AIP external API (v1.8.0 — PPDO-14). Like
/// <see cref="AipTreeMapper"/> and <see cref="AipFormRowBuilder"/>, this takes no repository
/// dependency and applies no scope — <c>ExternalAipReadService</c> owns which offices are
/// released and passes them in already decided.
/// </summary>
public static class ExternalAipMapper
{
    /// <summary>Formats a peso amount as the schema's money string — exactly two decimals, e.g. "1250000.00".</summary>
    public static string FormatMoney(decimal value) => value.ToString("F2", CultureInfo.InvariantCulture);

    /// <summary>Formats a quantity as the schema's quantity string — up to two decimals, no trailing zeros ("60", "2.5").</summary>
    public static string FormatQuantity(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    public static ExternalMoneyAmountsDto Amounts(decimal ps, decimal mooe, decimal co)
        => new(FormatMoney(ps), FormatMoney(mooe), FormatMoney(co), FormatMoney(ps + mooe + co));

    public static ExternalMoneyAmountsDto PrintedAmounts(AipPrintedAmountsDto printed)
        => new(FormatMoney(printed.Ps), FormatMoney(printed.Mooe), FormatMoney(printed.Co), FormatMoney(printed.Total));

    /// <summary>Maps an <see cref="AipOffice.Sector"/> string ("GENERAL"/"SOCIAL"/…) to the
    /// schema's (code, name) pair — different casing and shape from the stored value.</summary>
    public static ExternalSectorDto MapSector(string sector) => sector.Trim().ToUpperInvariant() switch
    {
        AipSector.General  => new ExternalSectorDto("1000", "General"),
        AipSector.Social   => new ExternalSectorDto("3000", "Social"),
        AipSector.Economic => new ExternalSectorDto("8000", "Economic"),
        AipSector.Others   => new ExternalSectorDto("9000", "Others"),
        _ => throw new InvalidOperationException($"Unknown AIP sector '{sector}'."),
    };

    /// <summary>
    /// Splits <see cref="AipActivity.CcTypologyCode"/> into the schema's array. Still a single
    /// free-text column, sometimes comma-separated for an activity tagged with more than one
    /// typology — see <see cref="ClimateChangeTypology"/>'s remarks. No join table exists yet
    /// (docs/external-api/README.md §6), so this is exactly what that column holds today.
    /// </summary>
    public static IReadOnlyList<string> SplitTypologyCodes(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// Resolves a funding source's display name for a legacy activity's own snapshot code —
    /// <paramref name="byId"/> is a small full-table lookup (<c>FundingSource</c> is a config
    /// table), not a per-activity query. Null <paramref name="fundingSourceId"/> or a since-
    /// removed row both fall back to code-only, matching the schema's "name: null only for a
    /// legacy row whose code never matched" note.
    /// </summary>
    public static ExternalFundingSourceDto? MapLegacyFundingSource(
        string? snapshotCode, int? fundingSourceId, IReadOnlyDictionary<int, FundingSource> byId)
    {
        if (string.IsNullOrWhiteSpace(snapshotCode)) return null;

        string? name = fundingSourceId is int id && byId.TryGetValue(id, out FundingSource? fs) ? fs.Name : null;
        return new ExternalFundingSourceDto(snapshotCode, name);
    }

    /// <summary>FY2028+ expenditure lines already carry both code and name as snapshots — no lookup needed.</summary>
    public static ExternalFundingSourceDto MapLineFundingSource(AipExpenditure line)
        // A line with no recorded fund is the edge case IAipExpenditureRepository's own docs call
        // out (a "fundless line", invisible to the ceiling check) — rare, but the schema requires
        // a non-null code on every line, so it gets an explicit sentinel rather than crashing.
        => new(
            string.IsNullOrWhiteSpace(line.FundingSourceSnapshot) ? "UNSPECIFIED" : line.FundingSourceSnapshot,
            line.FundingSourceNameSnapshot);

    /// <summary>
    /// An expenditure line's expense class, read off which of its own PS/MOOE/CO is non-zero
    /// rather than joined from <c>Account</c> — the line is the source of truth for its own
    /// figures regardless of whether its <c>AccountId</c> still resolves, and matches the schema's
    /// "exactly one of ps/mooe/co is normally non-zero" note.
    /// </summary>
    public static string ExpenseClassOf(AipExpenditure line)
    {
        if (line.Ps != 0) return "PS";
        if (line.Mooe != 0) return "MOOE";
        if (line.Co != 0) return "CO";
        return "MOOE"; // all-zero line — arbitrary but never reached by a real encoded row
    }

    public static ExternalProcurementItemDto MapProcurementItem(
        AipProcurementItem item, IReadOnlyDictionary<int, string?> stockCardNoByPriceIndexItemId)
    {
        string? stockCardNo = item.PriceIndexItemId is int piId
            ? stockCardNoByPriceIndexItemId.GetValueOrDefault(piId)
            : null;

        return new ExternalProcurementItemDto(
            item.Name, item.Unit, FormatMoney(item.UnitPrice), FormatQuantity(item.Qty),
            FormatQuantity(item.NumberOfDays), FormatMoney(item.LineTotal),
            stockCardNo, item.PriceIndexItemId is not null);
    }

    public static ExternalExpenditureDto MapExpenditure(
        AipExpenditure line,
        IReadOnlyList<AipProcurementItem> items,
        IReadOnlyDictionary<int, string?> stockCardNoByPriceIndexItemId) => new(
        new ExternalAccountDto(
            line.AccountNumberSnapshot ?? string.Empty,
            line.AccountTitleSnapshot ?? string.Empty,
            ExpenseClassOf(line)),
        MapLineFundingSource(line),
        Amounts(line.Ps, line.Mooe, line.Co),
        items.Select(i => MapProcurementItem(i, stockCardNoByPriceIndexItemId)).ToList());

    /// <summary>
    /// Maps one activity. <paramref name="expenditures"/> is empty for a legacy activity (the
    /// format carries none); <paramref name="aipFormat"/> decides whether <c>printedAmounts</c> is
    /// computed at all and whether <c>fundingSource</c> comes from the activity or stays null
    /// (build spec §2 decisions 5/6).
    /// </summary>
    public static ExternalActivityDto MapActivity(
        AipActivity activity,
        IReadOnlyList<AipExpenditure> expenditures,
        IReadOnlyDictionary<int, IReadOnlyList<AipProcurementItem>> itemsByExpenditureId,
        IReadOnlyDictionary<int, string?> stockCardNoByPriceIndexItemId,
        IReadOnlyDictionary<int, FundingSource> fundingSourcesById,
        string aipFormat,
        int fiscalYear)
    {
        bool isFy2028 = aipFormat == ExternalAipConstants.FormatFy2028;

        // FY2028+: the activity's own amounts are the sum of its expenditure lines (build spec
        // §2 decision 5 — always fresh, never trusts the recompute having already run).
        // Legacy: the activity carries its own figures directly; it has no lines at all.
        ExternalMoneyAmountsDto amounts = isFy2028
            ? Amounts(expenditures.Sum(e => e.Ps), expenditures.Sum(e => e.Mooe), expenditures.Sum(e => e.Co))
            : Amounts(activity.Ps ?? 0m, activity.Mooe ?? 0m, activity.Co ?? 0m);

        // printedAmounts is read from AipPrintedFigures — the Excel export's own source — against
        // the activity's STORED Ps/Mooe/Co, deliberately not re-derived from the summed lines
        // above (build spec §2 decision 3: "never re-derived"). Null entirely for legacy.
        ExternalMoneyAmountsDto? printedAmounts = isFy2028
            ? PrintedAmounts(AipPrintedFigures.ForActivity(activity, fiscalYear))
            : null;

        List<ExternalExpenditureDto> mappedExpenditures = isFy2028
            ? expenditures
                .Select(e => MapExpenditure(
                    e,
                    itemsByExpenditureId.GetValueOrDefault(e.Id, []),
                    stockCardNoByPriceIndexItemId))
                .ToList()
            : [];

        return new ExternalActivityDto(
            activity.RefCode,
            activity.Name,
            activity.IsSynthetic,
            activity.EsreCode,
            activity.ImplementingOffice,
            new ExternalScheduleDto(activity.StartDate, activity.EndDate),
            activity.ExpectedOutputs,
            isFy2028 ? null : MapLegacyFundingSource(activity.FundingSourceSnapshot, activity.FundingSourceId, fundingSourcesById),
            amounts,
            printedAmounts,
            // Base pesos, no uplift — the schema is explicit these never carry DECISION G's +30%.
            new ExternalClimateChangeDto(
                FormatMoney(activity.CcAdaptation ?? 0m),
                FormatMoney(activity.CcMitigation ?? 0m),
                SplitTypologyCodes(activity.CcTypologyCode)),
            mappedExpenditures);
    }
}
