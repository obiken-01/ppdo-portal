using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// The figures the AIP form <b>prints</b> for one activity (v1.8.0 — PPDO-73, and Phase 5's Excel).
///
/// <para>
/// Two rules land here together, and <see cref="AipRounding"/> documents both halves:
/// </para>
/// <list type="bullet">
/// <item><b>DECISION 9</b> — every figure rounded <b>up</b> to the thousand, then summed.</item>
/// <item><b>DECISION G</b> — MOOE and CO carry a fixed +30% uplift, FY2028+ only, presentation-only.
/// PS prints as entered. The climate-change columns are not uplifted either: the uplift is on MOOE
/// and CO (<c>AIP_Form_Spec.md</c> §6 #4).</item>
/// </list>
///
/// <para>
/// ⚠️ <b>Uplift, then round</b> — tracker G5, read from a bare "Yes" to an either/or question and
/// still provisional (<c>Phase_Plan.md</c> §12.8 Q1). If the answer turns out to be round-then-uplift,
/// <see cref="ForActivity"/> is the one line to change. ₱1,000,400 MOOE prints ₱1,301,000 this way,
/// ₱1,302,000 the other.
/// </para>
///
/// <para>
/// ⚠️ <b>Never assert <c>printed == 1.3 × checked</c>.</b> The ceiling compares the rounded
/// <i>base</i>; the form prints the rounded <i>uplift</i>. Once both are rounded they are not 1.3×
/// each other (<see cref="AipRounding"/>).
/// </para>
/// </summary>
public static class AipPrintedFigures
{
    /// <summary>DECISION G's fixed uplift. Not per-year, not configurable (tracker G2).</summary>
    public const decimal UpliftRate = 1.3m;

    /// <summary>A row with nothing to print.</summary>
    public static readonly AipPrintedAmountsDto Zero = new(0m, 0m, 0m, 0m, 0m, 0m);

    /// <summary>
    /// Whether the FY2028+ rules apply at all. ⚠️ FY≤2027 records keep their old shape and are not
    /// re-rendered under these rules (<c>AIP_Form_Spec.md</c> §8) — they print exact amounts.
    /// </summary>
    public static bool AppliesTo(int fiscalYear) => fiscalYear >= AipFiscalYears.FirstEnteredFiscalYear;

    /// <summary>
    /// One activity's printed figures, from its PS / MOOE / CO / CC columns — which are kept equal
    /// to the sums of its expenditure lines on every line write (<c>AipActivityTotalsService</c>).
    /// </summary>
    public static AipPrintedAmountsDto ForActivity(AipActivity activity, int fiscalYear)
    {
        decimal ps   = activity.Ps ?? 0m;
        decimal mooe = activity.Mooe ?? 0m;
        decimal co   = activity.Co ?? 0m;
        decimal cca  = activity.CcAdaptation ?? 0m;
        decimal ccm  = activity.CcMitigation ?? 0m;

        if (!AppliesTo(fiscalYear))
            return new AipPrintedAmountsDto(ps, mooe, co, ps + mooe + co, cca, ccm);

        decimal printedPs   = AipRounding.UpToThousand(ps);
        decimal printedMooe = AipRounding.UpToThousand(mooe * UpliftRate);
        decimal printedCo   = AipRounding.UpToThousand(co * UpliftRate);

        return new AipPrintedAmountsDto(
            printedPs,
            printedMooe,
            printedCo,
            // ⚠️ The sum of the printed figures, never a rounding of the exact total — column (11)
            // is labelled 8+9+10 on the form and must add up across the row.
            printedPs + printedMooe + printedCo,
            AipRounding.UpToThousand(cca),
            AipRounding.UpToThousand(ccm));
    }

    /// <summary>
    /// Adds rows that are already printed figures — an office subtotal from its activities, a sheet
    /// TOTAL from its offices. ⚠️ Never rounds again: every level of the form is a sum of the level
    /// below it.
    /// </summary>
    public static AipPrintedAmountsDto Sum(IEnumerable<AipPrintedAmountsDto> parts)
    {
        decimal ps = 0m, mooe = 0m, co = 0m, total = 0m, cca = 0m, ccm = 0m;
        foreach (AipPrintedAmountsDto p in parts)
        {
            ps    += p.Ps;
            mooe  += p.Mooe;
            co    += p.Co;
            total += p.Total;
            cca   += p.CcAdaptation;
            ccm   += p.CcMitigation;
        }
        return new AipPrintedAmountsDto(ps, mooe, co, total, cca, ccm);
    }
}
