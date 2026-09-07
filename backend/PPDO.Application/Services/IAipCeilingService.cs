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
    /// No-ops when the activity's office has no division to attribute the reservation to; guest
    /// offices are checked at office level and get no synthetic division row (V18-47 / PPDO-57).
    /// </summary>
    Task UpsertLedgerForActivityAsync(int aipActivityId, CancellationToken ct = default);
}
