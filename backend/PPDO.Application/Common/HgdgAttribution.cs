namespace PPDO.Application.Common;

/// <summary>
/// HGDG score → attributed GAD budget (v1.8.0 Demo 2.15 — PPDO-156, Investment_Proposal_Spec.md
/// decision 9). Pure/static and side-effect-free, like <see cref="WfpExpenditureCalculator"/>.
///
/// <para>
/// The attribution bands of the PCW Harmonized Gender and Development Guidelines:
/// </para>
/// <list type="table">
///   <item><description>0 – 3.9   → 0%   (GAD is invisible)</description></item>
///   <item><description>4.0 – 7.9 → 25%  (promising GAD prospects)</description></item>
///   <item><description>8.0 – 14.9 → 50% (gender-sensitive)</description></item>
///   <item><description>15.0 – 19.9 → 75% (gender-responsive)</description></item>
///   <item><description>20.0      → 100% (fully gender-responsive)</description></item>
/// </list>
///
/// <para>
/// ⚠️ <b>Merge gate (spec open follow-up): confirm these bands against the PCW HGDG manual.</b>
/// A wrong band is a constant change here and nothing else.
/// </para>
///
/// <para>
/// The score is typed by the user. It is never computed from checklist answers (spec §7 non-goal).
/// Only the attributed budget is derived, and it is never typed.
/// </para>
/// </summary>
public static class HgdgAttribution
{
    public const decimal MinScore = 0m;
    public const decimal MaxScore = 20m;

    /// <summary>
    /// The attribution percentage for a score, or null when there is no score or it is outside
    /// 0–20. The validator refuses out-of-range scores on save; null here keeps a bad stored value
    /// from printing a figure.
    /// </summary>
    public static int? Percent(decimal? score)
    {
        if (score is not decimal s || s < MinScore || s > MaxScore) return null;

        // Lower bounds, so a score between the published one-decimal bands (3.95) falls in the
        // lower band: it has not reached 4.0.
        return s switch
        {
            < 4m  => 0,
            < 8m  => 25,
            < 15m => 50,
            < 20m => 75,
            _     => 100,
        };
    }

    /// <summary>
    /// Project Cost × the score's percentage, in pesos to the centavo (MidpointRounding.AwayFromZero).
    /// Null with no score, so all three GAD figures print blank rather than 0.00.
    /// </summary>
    public static decimal? AttributedBudget(decimal? score, decimal projectCost)
        => Percent(score) is int percent
            ? decimal.Round(projectCost * percent / 100m, 2, MidpointRounding.AwayFromZero)
            : null;
}
