using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Applies <see cref="BudgetPlanningScope"/> to the shape an AIP read actually has — offices and
/// programs (v1.8.0 Phase 2 — V18-39 / PPDO-38). <b>This is <see cref="BudgetPlanningScope"/>'s
/// first consumer</b>; it was built by RAL-250 and had none until now.
///
/// <para>
/// AIP is the first feature where division scoping applies to <b>some callers only</b>:
/// </para>
/// <list type="table">
///   <item><description>WFP  — office always, division <b>always</b></description></item>
///   <item><description>LDIP — office always, division <b>never</b></description></item>
///   <item><description><b>AIP  — office always, division only when the caller is PPDO</b></description></item>
/// </list>
///
/// <para>
/// ⚠️ <b>Both failure directions are silent and neither shows up in a diff.</b> Honour division for
/// a guest office and they see a fraction of their own AIP and report missing data; ignore it for
/// PPDO and a division-scoped encoder sees every division's figures. Nothing throws. That is why
/// the rule lives here once, tested directly, rather than being re-derived at each call site —
/// re-deriving it by hand at each site is exactly what PPDO-30's division-axis leak was.
/// </para>
///
/// <para>
/// <b>The two axes attach to different levels, which is the part worth reading twice.</b> The
/// office axis filters <see cref="AipOffice"/> rows on their ownership FK. The division axis
/// filters <see cref="AipProgram"/> rows, because division of work is carried on the <i>program</i>
/// through <see cref="ProgramDivision"/> — spec §2 decision 4, the same thing WFP does. There is no
/// division column on an AIP office and there deliberately is not one.
/// </para>
///
/// <para>
/// ⚠️ <b>The division filter applies only to the host office's own AIP offices.</b> A division
/// belongs to an office, so it can only narrow that office's work. A PPDO caller scoped to the
/// Planning Division still sees every guest office's AIP in full — PPDO reviews all of them, and
/// its internal division of labour says nothing about GSO's programs. This matches what
/// <c>BudgetPlanningDashboardService</c> already does: it computes its per-division AIP rollups
/// over <c>hostAipOfficeIds</c> only.
/// </para>
/// </summary>
public sealed class AipReadScope
{
    private readonly BudgetPlanningScope _scope;
    private readonly int _hostOfficeId;

    private AipReadScope(BudgetPlanningScope scope, int hostOfficeId)
    {
        _scope        = scope;
        _hostOfficeId = hostOfficeId;
    }

    /// <summary>Resolves both axes for a caller. <see cref="User.Office"/> must be loaded.</summary>
    public static AipReadScope Resolve(User caller)
        => new(BudgetPlanningScope.Resolve(caller), caller.OfficeId ?? OfficeScope.NoOffice);

    /// <summary>
    /// True when the caller's division narrows anything at all — i.e. a host-office caller who is
    /// not themselves see-all. False for every guest-office caller, whatever division they carry.
    /// </summary>
    public bool DivisionNarrows => _scope.DivisionIsScopingAxis && !_scope.Division.SeeAll;

    /// <summary>
    /// The AIP offices this caller may see.
    ///
    /// <para>
    /// A caller with no office resolves to <see cref="OfficeScope.NoOffice"/> (id 0), which no real
    /// office row carries, so this returns empty. That is deliberate: null <c>office_id</c> means
    /// unassigned and sees nothing (DECISION F / RAL-258). Older comments claiming a null office id
    /// means "PPDO, sees everything" are stale.
    /// </para>
    ///
    /// <para>
    /// An AIP office with a null ownership FK — a row the V18-32 backfill could not match — is
    /// returned to a host caller (who sees all) and to nobody else. It cannot be claimed by a guest
    /// office just because a ref code looks similar.
    /// </para>
    /// </summary>
    public IReadOnlyList<AipOffice> FilterOffices(IReadOnlyList<AipOffice> offices)
        => _scope.Office.SeeAll
            ? offices
            : offices.Where(o => o.OfficeId == _scope.Office.OfficeId).ToList();

    /// <summary>
    /// The programs this caller may see, out of <paramref name="programs"/> belonging to
    /// <paramref name="officesInScope"/>.
    ///
    /// <para>
    /// <paramref name="hostAssignments"/> are the <see cref="ProgramDivision"/> rows for the host
    /// office; pass an empty list when <see cref="DivisionNarrows"/> is false, since they are not
    /// read then and loading them would be a wasted query.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>An unassigned host program is excluded from a division-scoped view, not shared into
    /// every division.</b> It belongs to no division row, and spreading it would make each
    /// division's figures overlap. The allocation-setup panel surfaces those as an "unassigned"
    /// count — that is where they are meant to be noticed.
    /// </para>
    /// </summary>
    public IReadOnlyList<AipProgram> FilterPrograms(
        IReadOnlyList<AipProgram> programs,
        IReadOnlyList<AipOffice> officesInScope,
        IReadOnlyList<ProgramDivision> hostAssignments)
    {
        if (!DivisionNarrows) return programs;

        // Only the host office's own AIP offices are subject to the division axis.
        HashSet<int> hostAipOfficeIds = officesInScope
            .Where(o => o.OfficeId == _hostOfficeId)
            .Select(o => o.Id)
            .ToHashSet();

        if (hostAipOfficeIds.Count == 0) return programs;

        // SeeNothing (a host Staff user with no division) narrows the host's own programs to none.
        // Guest offices' programs are untouched either way — see the type's remarks.
        HashSet<string> allowed = _scope.Division.SeeNothing
            ? []
            : hostAssignments
                .Where(a => a.DivisionId == _scope.Division.DivisionId)
                .Select(a => a.ProgramRefCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return programs
            .Where(p => !hostAipOfficeIds.Contains(p.OfficeId) || allowed.Contains(p.RefCode))
            .ToList();
    }

    /// <summary>
    /// The config office id whose <see cref="ProgramDivision"/> rows
    /// <see cref="FilterPrograms"/> needs, or null when the division axis does not apply and no
    /// query should be issued.
    /// </summary>
    public int? HostOfficeIdForAssignments => DivisionNarrows ? _hostOfficeId : null;
}
