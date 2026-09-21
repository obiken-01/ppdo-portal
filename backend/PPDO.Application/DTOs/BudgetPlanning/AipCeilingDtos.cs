namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One office's AIP ceiling position (V18-46 / PPDO-56) — what the submit checklist shows before
/// the user presses submit, and what <c>GET /aip/{id}/ceiling</c> returns.
///
/// Every figure is <b>pesos</b>, <b>General Fund only</b>, and <b>base</b> — the +30% uplift is
/// presentation-only and never appears here (DECISION G / tracker G3).
///
/// ⚠️ <see cref="Remaining"/> may be <b>negative</b> and must be rendered as such. After PBO cuts a
/// ceiling below encoded work that negative is the only signal the office gets, and it is exactly
/// the state that blocks submit. No <c>Math.Max(0, …)</c> anywhere between here and the screen.
/// </summary>
/// <param name="GeneralFundId">
/// The General Fund config row's id, or null when no fund is marked as General Fund. Null means the
/// check could not be performed rather than that it passed.
/// </param>
/// <param name="CeilingSet">
/// Whether PBO has set a General Fund ceiling for this office and fiscal year.
///
/// ⚠️ <b>False is not "unlimited".</b> A missing ceiling is treated as <b>zero</b>, the same rule
/// that governs a missing allocation row (<c>GetDivisionAllocationAsync</c> → <c>0m</c>). The flag
/// exists so the UI can say "PBO has not set your ceiling" instead of "you are ₱X over", which
/// sends the encoder to delete work that is not the problem.
/// </param>
/// <param name="Ceiling">The office's General Fund ceiling in pesos. Zero when <paramref name="CeilingSet"/> is false.</param>
/// <param name="EncodedBaseRounded">
/// Σ over the office's activities of <c>roundUpToThousand(MOOE) + roundUpToThousand(CO)</c>, General
/// Fund lines only, PS excluded.
///
/// ⚠️ Rounded per activity <i>then</i> summed — the order the printed form is built in. Summing
/// first and rounding once gives a smaller number that disagrees with the document.
/// </param>
/// <param name="Remaining"><see cref="Ceiling"/> − <see cref="EncodedBaseRounded"/>. May be negative.</param>
/// <param name="WithinCeiling">Whether submit may proceed on ceiling grounds.</param>
public sealed record AipCeilingStatusDto(
    int?    GeneralFundId,
    bool    CeilingSet,
    decimal Ceiling,
    decimal EncodedBaseRounded,
    decimal Remaining,
    bool    WithinCeiling);
