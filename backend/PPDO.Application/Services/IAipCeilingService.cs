using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// The AIP ceiling check (V18-46 / PPDO-56) — narrower than its name reads, and wrong in four
/// specific ways if written from intuition. Each is pinned by a named test in
/// <c>AipCeilingServiceTests</c>.
///
/// <list type="number">
/// <item><b>General Fund ONLY.</b> ↩️ DECISION H (ceilings on every fund) was withdrawn
/// 2026-08-26, one day after being adopted. This has flipped twice — read
/// <c>Phase_Plan.md</c> §12.3 before re-deriving it from meeting notes.</item>
/// <item><b>PS is exempt</b>, as an expense <i>class</i>, on top of the fund restriction. The
/// check sums <c>mooe + co</c>.</item>
/// <item><b>Non-GF funds are excluded by an EXPLICIT rule</b>, never by absent configuration.
/// <c>GetDivisionAllocationAsync</c> resolves a missing allocation to <c>0m</c>, so a blank row
/// means <b>zero</b>, not <b>unlimited</b> — leaving non-GF funds unconfigured would silently
/// forbid them rather than ignore them.</item>
/// <item><b>Rounded base figures.</b> Every figure is rounded up to the thousand before summing
/// (<see cref="Common.AipRounding"/>), and the +30% uplift is presentation-only and never part of
/// the comparison (DECISION G / tracker G3).</item>
/// </list>
///
/// <b>⚠️ When it runs: at SUBMIT, never during entry</b> (DECISION C). Over-ceiling encoding is
/// allowed and expected; V18-49's submit checklist is the gate, and it calls this.
///
/// <b>⚠️ This does not replace <see cref="IWfpCeilingService"/></b> — zero diff there. That check
/// stays live for FY2028+, and a WFP expenditure remains bound by <i>the lesser</i> of its AIP
/// activity amount and the fund's currently remaining allocation.
///
/// <b>⚠️ Remaining is never clamped at zero</b>, here or in any DTO or UI above it. After PBO cuts
/// a ceiling below encoded work it is legitimately negative, and that negative is the only signal
/// the office gets (A5-b).
/// </summary>
public interface IAipCeilingService
{
    /// <summary>
    /// Read-only ceiling status for one office's AIP — what the submit checklist shows
    /// <i>before</i> the user presses submit, and what <c>GET /aip/{id}/ceiling</c> returns.
    ///
    /// <see cref="AipCeilingStatusDto.Remaining"/> may be negative.
    /// </summary>
    Task<AipCeilingStatusDto> GetStatusAsync(
        int aipOfficeId, CancellationToken ct = default);

    /// <summary>
    /// The same status, addressed the way the <b>Provincial Budget Office</b> addresses an office
    /// — by config office and fiscal year, rather than by a row in one AIP record
    /// (V18-48 / PPDO-58).
    ///
    /// <para>
    /// PBO sets ceilings for offices it does not belong to, so it cannot use
    /// <see cref="GetStatusAsync"/>'s address: that takes an <c>aipOfficeId</c>, and the endpoint
    /// above it resolves the caller's <i>own</i> group row. This overload exists so a cross-office
    /// authority can ask "what has that office encoded against the ceiling I am about to cut?"
    /// — the question PBO otherwise has no way to answer, since a cut is non-destructive and shows
    /// them nothing (A5-b).
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Returns null when the question cannot be asked, never a zeroed status.</b> No AIP
    /// record for that fiscal year, or that office holding no rows in it, is <i>"nothing to
    /// compare against"</i> — reporting it as an encoded total of ₱0 would tell PBO the office had
    /// encoded nothing, which is a different and reassuring claim. Same distinction as
    /// <c>CeilingSet</c>, one level up.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Authorisation is the caller's job</b>, as everywhere else in this service. This
    /// answers about any office it is asked about; the endpoint gates it on the cross-office grant.
    /// </para>
    /// </summary>
    Task<AipCeilingStatusDto?> GetStatusForOfficeAsync(
        int configOfficeId, int fiscalYear, CancellationToken ct = default);

    /// <summary>
    /// The submit-time gate. Returns <c>null</c> when the office is within its General Fund
    /// ceiling, or a message naming <b>the fund, the ceiling, the encoded total and the
    /// overage</b> — not the bare phrase "over ceiling", which tells an encoder nothing about how
    /// much to remove.
    /// </summary>
    Task<string?> ValidateForSubmitAsync(
        int aipOfficeId, CancellationToken ct = default);

    /// <summary>
    /// Upserts the <c>aip_division_allocation_ledger</c> rows for one activity, recomputing
    /// <c>ReservedAmount</c> per funding source from that activity's current expenditure lines.
    ///
    /// A fund the activity no longer uses is recomputed down to zero rather than left stale at its
    /// last positive amount — the staleness bug RAL-154 fixed on the WFP side.
    ///
    /// <b>⚠️ Two different situations write no row here, and they are not the same</b>
    /// (V18-47 / PPDO-57):
    /// <list type="bullet">
    /// <item><b>A guest office.</b> Division is not a scoping axis for them at all
    /// (<c>Permission_Matrix.md</c> §3.1, PPDO-4) — not "no divisions configured yet". They are
    /// bound by the office-level ceiling in <see cref="GetStatusAsync"/> and get <b>no synthetic
    /// division row</b>. Correct, expected, silent.</item>
    /// <item><b>A host-office program no <c>ProgramDivision</c> row claims.</b> A
    /// misconfiguration: it reserves nothing against any division allocation. Logged at
    /// <c>Warning</c>, and fixed on the allocation-setup panel.</item>
    /// </list>
    /// Resolving both to a null division id, as this did before PPDO-57, made the second
    /// indistinguishable from the first and therefore invisible.
    /// </summary>
    Task UpsertLedgerForActivityAsync(int aipActivityId, CancellationToken ct = default);
}
