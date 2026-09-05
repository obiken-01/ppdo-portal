namespace PPDO.Application.Common;

/// <summary>
/// Which fiscal years this system will build a WFP for (v1.8.0 Phase 3 — V18-81 / PPDO-49).
///
/// <para>
/// <b>Why FY2028+ is refused, and why "yet" is the important word.</b> It is genuinely unknown
/// whether the FY2028 WFP is built here or in <b>GSO's</b> system — the approved AIP may simply be
/// handed over, and GSO's system built the FY2027 WFP for other offices. Tracker <b>C2</b>, rated
/// Blocker for exactly that reason.
/// </para>
///
/// <para>
/// Until it is answered, an FY2028 WFP built here would draw on the same <c>DivisionAllocation</c>
/// the AIP has already reserved against, and the two would <b>double-count silently</b>. Refusing
/// the year makes that <i>impossible</i> rather than <i>silently wrong</i> — which is what lets
/// V18-45 ship an AIP reservation ledger with no relief/netting mechanism at all. With no FY2028
/// WFP in this system, there is nothing to net against.
/// </para>
///
/// <para>
/// ⚠️ <b>This is a deferral, not a prohibition, and the message says so.</b> The answer to C2 may
/// well make FY2028 WFPs supported here, at which point this type is deleted rather than amended.
/// A message reading "not allowed" would tell the province something untrue about their own
/// process.
/// </para>
///
/// <para>
/// ⚠️ <b>The break year is <see cref="AipFiscalYears.FirstEnteredFiscalYear"/> and is not restated
/// here.</b> That coupling is deliberate and real, not incidental: the reason FY2028 is uncertain
/// is precisely that the AIP changes shape there. Should the province slip the break, the WFP
/// refusal must move with it in the same edit — which is why
/// <c>AipFiscalYearsTests.TheBreakYear_IsHardcodedInExactlyOnePlace</c> fails the build if the literal
/// reappears anywhere, this file included.
/// </para>
///
/// <para>
/// ⚠️ <b><see cref="WfpCeilingService"/> is deliberately untouched by all of this.</b> The plan
/// once proposed retiring its allocation check for FY2028+; that was reversed 2026-08-26. The
/// check lives in four already-FY-parameterised methods, so "retire for FY2028+" would <b>add four
/// conditionals rather than delete code</b> — and it is the only fund-scoped check in the system.
/// With this refusal in place none of those four is ever reached with a FY2028 year, so the check
/// is already inert for those years: retiring it would buy nothing and cost the fund-scoped guard.
/// </para>
/// </summary>
public static class WfpSupportedYears
{
    /// <summary>
    /// Why a WFP may not be built for <paramref name="fiscalYear"/> in this system, or null when
    /// it may.
    ///
    /// <para>
    /// Call sites read as:
    /// <code>
    /// if (WfpSupportedYears.RefuseCreate(dto.FiscalYear) is string unsupported)
    ///     return ServiceResult&lt;T&gt;.BadRequest(unsupported);
    /// </code>
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Call it as the FIRST statement of the method.</b> Both WFP write paths are
    /// <i>find-or-create</i>: a refusal that arrives after the record has been added is the
    /// original bug with an error message attached. This is the same failure V18-37 found in
    /// <c>CopyOfficeFromPriorYearAsync</c> and <c>SeedProgramsFromLdipAsync</c>, where the guard
    /// had to move to the top for the same reason.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>The refusal covers the whole year, not only the create branch.</b> Blocking creation
    /// alone would leave any FY2028 record that already exists freely editable, and an edit draws
    /// on the allocation exactly as a create does — so the double-count this exists to prevent
    /// would still be reachable. V18-37 made the same call for the same reason: its guard sits
    /// above the re-upload branch rather than beside the create.
    /// </para>
    /// </summary>
    public static string? RefuseCreate(int fiscalYear)
        => fiscalYear >= AipFiscalYears.FirstEnteredFiscalYear
            ? $"FY {fiscalYear} Work Financial Plans are not built in this portal yet. The FY "
              + $"{fiscalYear} AIP is entered here, but its WFP is not — where that document is "
              + "produced has not been settled. FY "
              + $"{AipFiscalYears.FirstEnteredFiscalYear - 1} and earlier are unaffected."
            : null;
}
