using System.Globalization;

namespace PPDO.Application.Common;

/// <summary>
/// The largest peso figure an AIP expenditure line or procurement item may carry (PPDO-293).
///
/// <para>
/// <b>Why a cap at all.</b> Money columns are <c>decimal(18,2)</c>, so a figure past 16 digits was a
/// SQL overflow and came back as a 500. Long before that, past 2^53 centavos, the browser starts
/// rounding the figure it shows. ₱999,999,999,999.99 sits well inside both, and far above any real
/// line in a provincial AIP, so it only ever catches a typo or a bad API call. Chosen by Ralph,
/// 2026-11-03.
/// </para>
///
/// <para>
/// ⚠️ The page holds the same figure as <c>AIP_AMOUNT_MAX</c> in <c>frontend/src/lib/aip.ts</c>.
/// Change one and change the other.
/// </para>
/// </summary>
public static class AipAmountLimit
{
    public const decimal Max = 999_999_999_999.99m;

    /// <summary>The cap as the message shows it: "₱999,999,999,999.99".</summary>
    public static string Display => "₱" + Max.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>
    /// <c>a × b × c</c> is at most <see cref="Max"/>. ⚠️ Decimal multiplication throws past
    /// ~7.9 × 10^28 rather than saturating, and quantity and days have no limit of their own, so
    /// the overflow is caught and counted as "over" instead of escaping as a 500.
    /// </summary>
    public static bool ProductWithin(decimal a, decimal b, decimal c)
    {
        try { return a * b * c <= Max; }
        catch (OverflowException) { return false; }
    }
}
