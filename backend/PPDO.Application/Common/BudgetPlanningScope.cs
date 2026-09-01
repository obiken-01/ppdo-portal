using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Resolved data scope for a Budget Planning query — the office axis and the division axis
/// together (v1.8.0 — RAL-250).
///
/// <b>The rule:</b> division is a scoping axis <i>only for host-office (PPDO) callers</i>.
/// PPDO separates its AIP and WFP work by division; every other office sees all of its own
/// work regardless of which division the user sits in.
///
/// This is what neither existing feature does. WFP scopes by <c>(OfficeId, DivisionId)</c> with
/// division always participating; LDIP scopes by <c>OfficeId</c> alone with division never
/// participating. Both are wrong for one of the two caller kinds, which is why the rule lives
/// here once rather than being re-derived at each call site.
///
/// A guest-office user can legitimately carry a <see cref="User.DivisionId"/> — divisions are
/// office-scoped, so their office may well have them. It simply must not narrow what they see.
///
/// <b>⚠️ Always consume both axes together.</b> For a guest-office caller <see cref="Division"/>
/// is <see cref="DivisionScope.All"/>, which read on its own says "no division filter — every
/// division". That is only safe because <see cref="Office"/> pins the caller to one office in the
/// same query. Taking <c>.Division</c> alone and dropping <c>.Office</c> turns this struct into a
/// see-everything scope. Prefer <see cref="Resolve"/>'s result as a unit.
///
/// <b>⚠️ The two null rules, side by side — they are opposites, and copying the wrong one is the
/// mistake this section exists to prevent:</b>
/// <list type="bullet">
///   <item><description>
///     <see cref="DivisionScope"/>: a null <see cref="User.DivisionId"/> on a Staff user means
///     <i>unassigned</i> → <see cref="DivisionScope.Nothing"/>, an EMPTY result set. Never
///     "all divisions".
///   </description></item>
///   <item><description>
///     <see cref="OfficeScope"/>: a null <see cref="User.OfficeId"/> also means <i>unassigned</i>
///     → <see cref="OfficeScope.NoOffice"/>, which matches nothing. Cross-office authority comes
///     from <see cref="Office.IsHostOffice"/>, not from the null.
///   </description></item>
/// </list>
/// The two now agree: null means unassigned on both axes, and unassigned sees nothing. That is
/// new. Until DECISION F (RAL-258) a null office id positively meant "PPDO, sees everything" —
/// the inverse of the division rule — and any code or comment still asserting that is stale.
/// See <see cref="OfficeScope"/>'s own remarks before "restoring" it.
///
/// No feature consumes this yet, deliberately: Phase 3 reads it, and it is far cheaper to settle
/// before three call sites each invent their own version.
/// </summary>
public readonly struct BudgetPlanningScope
{
    private BudgetPlanningScope(OfficeScope office, DivisionScope division, bool divisionIsScopingAxis)
    {
        Office                = office;
        Division              = division;
        DivisionIsScopingAxis = divisionIsScopingAxis;
    }

    /// <summary>The office filter. Always applies.</summary>
    public OfficeScope Office { get; }

    /// <summary>
    /// The division filter. <see cref="DivisionScope.All"/> — no filter — whenever
    /// <see cref="DivisionIsScopingAxis"/> is false. Only meaningful alongside
    /// <see cref="Office"/>; see the type's remarks.
    /// </summary>
    public DivisionScope Division { get; }

    /// <summary>
    /// Whether division narrows this caller's results at all. True only for host-office (PPDO)
    /// callers. Exposed so a caller can branch on the rule itself — for a grouped-by-division
    /// view, say — rather than inferring it from <see cref="DivisionScope.SeeAll"/>, which is
    /// also true for a PPDO Admin and means something different.
    /// </summary>
    public bool DivisionIsScopingAxis { get; }

    /// <summary>
    /// Resolves both axes for a user.
    ///
    /// Host-office (PPDO) caller  → office <see cref="OfficeScope.All"/>, division resolved
    ///                              normally by <see cref="DivisionScope.Resolve"/>.
    /// Guest-office caller        → office scoped to their own office, division
    ///                              <see cref="DivisionScope.All"/> (no division filter).
    /// Caller with no office      → office <see cref="OfficeScope.NoOffice"/>, which matches
    ///                              nothing; the division axis is irrelevant either way.
    ///
    /// Requires <see cref="User.Office"/> to be loaded (JwtMiddleware guarantees it). If it is
    /// not, <see cref="OfficeScope.IsHostOfficeUser"/> answers false and the caller is treated
    /// as a guest-office user — more restrictive, never less.
    /// </summary>
    public static BudgetPlanningScope Resolve(User user)
    {
        bool divisionIsScopingAxis = OfficeScope.IsHostOfficeUser(user);

        return new BudgetPlanningScope(
            OfficeScope.Resolve(user),
            divisionIsScopingAxis ? DivisionScope.Resolve(user) : DivisionScope.All,
            divisionIsScopingAxis);
    }
}
