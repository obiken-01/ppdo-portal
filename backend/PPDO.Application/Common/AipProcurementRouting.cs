namespace PPDO.Application.Common;

/// <summary>
/// Where an itemised AIP expenditure line's money goes (V18-80 / PPDO-54).
///
/// <para>
/// <b>The question WFP never had to answer.</b> A WFP expenditure carries one <c>amount</c> per
/// period, so its procurement items simply add into it
/// (<c>mergeWfpPeriodAndItemAmounts</c>). An AIP expenditure carries <b>three</b> columns —
/// PS, MOOE, CO — so an itemised line has to say which one Σ line-total belongs in. It belongs in
/// the one matching the <b>account's</b> expense class, because that is what an expense class
/// <i>is</i>: the account already declares whether it is Personal Services, MOOE or Capital Outlay
/// (<c>AccountService</c> derives it from the account number's 5-01/5-02/5-03 prefix).
/// </para>
///
/// <para>
/// ⚠️ <b>The items drive the line, not the other way round.</b> Once a line has items, its amount
/// is derived and the encoder's typed PS/MOOE/CO are discarded rather than added to. Adding them
/// would let an encoder who types the total <i>and</i> itemises it double the line's cost with
/// nothing to catch it — and the two figures would disagree the moment one was edited, which is
/// the drift <c>AipExpenditure.Total</c> and RAL-144 exist to prevent.
/// </para>
///
/// <para>
/// ⚠️ <b>An unrecognised expense class is refused, never defaulted to MOOE.</b> Guessing puts real
/// money in the wrong column of a printed statutory document, and MOOE is the column the General
/// Fund ceiling is computed from — so the guess would also silently move the office's ceiling
/// consumption.
/// </para>
/// </summary>
public static class AipProcurementRouting
{
    /// <summary>The expense classes an account may declare, as stored by <c>AccountService</c>.</summary>
    public const string Ps   = "PS";
    public const string Mooe = "MOOE";
    public const string Co   = "CO";

    /// <summary>
    /// Splits <paramref name="total"/> into the PS / MOOE / CO triple, all of it in the column
    /// named by <paramref name="expenseClass"/> and zero in the other two.
    ///
    /// Returns null when the expense class is missing or unrecognised — the caller refuses the
    /// write rather than picking a column. See this type's remarks.
    /// </summary>
    public static (decimal Ps, decimal Mooe, decimal Co)? Route(string? expenseClass, decimal total)
        => expenseClass?.Trim().ToUpperInvariant() switch
        {
            Ps   => (total, 0m, 0m),
            Mooe => (0m, total, 0m),
            Co   => (0m, 0m, total),
            _    => null,
        };
}
