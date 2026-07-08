namespace PPDO.Application.Common;

/// <summary>
/// The reserve rule (v1.4 WFP Rework — §6, RAL-121): a single global rate, hard-coded as a
/// constant rather than a config table — nothing in the requirements draft describes
/// per-office/per-fiscal-year variation, so a tiny single-row config table would be
/// unnecessary indirection for a value that has never changed since the v1.1 prototype.
///
/// Applied against the expenditure's NET appropriation (ΣQ1–Q4 — what the periods/procurement
/// items sum to), not the derived TotalAppropriation. §6 says "10% × the line's own Total", but
/// this ticket's Total = Net + Reserved (RAL-120) — capping against Total would make the rate
/// self-referential (Reserve ≤ rate × (Net + Reserve) solves to ~11.1% of Net, not a clean 10%).
/// Net is the pre-reserve entered budget and is what "10% of Operational Expenses" means in
/// practice, so both the default and the cap are computed against it.
/// </summary>
public static class WfpReserveRule
{
    /// <summary>10% — see §6 of docs/v1.4/WFP_Rework_Requirements_Draft.md.</summary>
    public const decimal Rate = 0.10m;

    /// <summary>Rounds to the nearest centavo, matching the rest of the WFP money math.</summary>
    public static decimal Cap(decimal net) => Math.Round(net * Rate, 2);
}
