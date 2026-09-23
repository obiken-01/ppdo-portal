using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Applies the office and division axes to the shape an AIP read actually has — offices and
/// programs (v1.8.0 Phase 2 — V18-39 / PPDO-38).
///
/// <para>
/// AIP is the first feature where division scoping applies to <b>some callers only</b>:
/// </para>
/// <list type="table">
///   <item><description>WFP  — office always, division <b>always</b></description></item>
///   <item><description>LDIP — office always, division <b>never</b></description></item>
///   <item><description><b>AIP  — office always, division only when the caller has one to be scoped by</b></description></item>
/// </list>
///
/// <para>
/// ⚠️ <b>Both failure directions are silent and neither shows up in a diff.</b> Honour division
/// for an office that has none configured and its people see a fraction of their own AIP and
/// report missing data; ignore it for a caller who genuinely has one and they see figures outside
/// their division. Nothing throws. That is why the rule lives here once, tested directly, rather
/// than being re-derived at each call site — re-deriving it by hand at each site is exactly what
/// PPDO-30's division-axis leak was.
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
/// ⚠️ <b>The division filter applies only to the caller's own AIP offices.</b> A division belongs
/// to an office, so it can only narrow that office's work. A PPDO caller scoped to the Planning
/// Division still sees every guest office's AIP in full — PPDO reviews all of them, and its
/// internal division of labour says nothing about GSO's programs. This matches what
/// <c>BudgetPlanningDashboardService</c> already does: it computes its per-division AIP rollups
/// over <c>hostAipOfficeIds</c> only, for the host caller's own office.
/// </para>
///
/// <para>
/// ⚠️ <b>Fixed by PPDO-134 — this used to read "host office" where it meant "the caller's own
/// office," and those were the same thing only while PPDO was the sole office with divisions.</b>
/// PPDO-123 let a guest office assign divisions to its own users, which ended that equivalence:
/// the old code gated the whole division axis on <c>OfficeScope.IsHostOfficeUser</c>, so a
/// division-scoped guest encoder was never narrowed at all — a data leak, not a convenience.
/// The axis is now keyed to the caller's own office (<see cref="OfficeScope.Resolve"/>'s
/// <c>OfficeId</c>), host or guest alike. One asymmetry remains and is deliberate: a HOST Staff
/// member with no division sees none of the host's own programs (matches every other
/// <see cref="DivisionScope"/> consumer — unassigned means unassigned), but a GUEST Staff member
/// with no division sees ALL of their office's programs. Most guest offices have no divisions
/// configured yet, and a filter keyed to nothing must not read as "nothing to show."
/// </para>
/// </summary>
public sealed class AipReadScope
{
    private readonly OfficeScope _office;
    private readonly DivisionScope _division;
    private readonly bool _isHostOfficeCaller;
    private readonly int _callerOfficeId;

    private AipReadScope(OfficeScope office, DivisionScope division, bool isHostOfficeCaller, int callerOfficeId)
    {
        _office              = office;
        _division            = division;
        _isHostOfficeCaller  = isHostOfficeCaller;
        _callerOfficeId      = callerOfficeId;
    }

    /// <summary>Resolves both axes for a caller. <see cref="User.Office"/> must be loaded.</summary>
    public static AipReadScope Resolve(User caller) => new(
        OfficeScope.Resolve(caller),
        DivisionScope.Resolve(caller),
        OfficeScope.IsHostOfficeUser(caller),
        caller.OfficeId ?? OfficeScope.NoOffice);

    /// <summary>
    /// True when the caller's division narrows anything at all.
    ///
    /// Admin/SuperAdmin (<see cref="DivisionScope.SeeAll"/>) are never narrowed, host office or
    /// guest office alike — an office's administrator oversees every division of its own work.
    ///
    /// A Staff caller with an assigned division is always narrowed, host or guest.
    ///
    /// A Staff caller with NO division is narrowed only when they sit in the HOST office — see the
    /// type's remarks for why a guest office's unassigned Staff member is the one case that is
    /// deliberately NOT narrowed (empty division config must not read as empty AIP).
    /// </summary>
    public bool DivisionNarrows =>
        !_division.SeeAll && (_division.DivisionId is not null || _isHostOfficeCaller);

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
        => _office.SeeAll
            ? offices
            : offices.Where(o => o.OfficeId == _office.OfficeId).ToList();

    /// <summary>
    /// The programs this caller may see, out of <paramref name="programs"/> belonging to
    /// <paramref name="officesInScope"/>.
    ///
    /// <para>
    /// <paramref name="ownAssignments"/> are the <see cref="ProgramDivision"/> rows for the
    /// caller's OWN office (see <see cref="OfficeIdForAssignments"/>); pass an empty list when
    /// <see cref="DivisionNarrows"/> is false, since they are not read then and loading them would
    /// be a wasted query.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>An unassigned host-Staff program is excluded from a division-scoped view, not shared
    /// into every division.</b> It belongs to no division row, and spreading it would make each
    /// division's figures overlap. The allocation-setup panel surfaces those as an "unassigned"
    /// count — that is where they are meant to be noticed. This only applies to a HOST caller,
    /// since a guest caller with no assignments is not narrowed at all (see remarks).
    /// </para>
    /// </summary>
    public IReadOnlyList<AipProgram> FilterPrograms(
        IReadOnlyList<AipProgram> programs,
        IReadOnlyList<AipOffice> officesInScope,
        IReadOnlyList<ProgramDivision> ownAssignments)
    {
        if (!DivisionNarrows) return programs;

        // Only the caller's own AIP offices are subject to the division axis.
        HashSet<int> ownAipOfficeIds = officesInScope
            .Where(o => o.OfficeId == _callerOfficeId)
            .Select(o => o.Id)
            .ToHashSet();

        if (ownAipOfficeIds.Count == 0) return programs;

        // A host Staff member with no division (DivisionId null but DivisionNarrows still true,
        // since _isHostOfficeCaller carried it) narrows the host's own programs to none. A guest
        // caller only reaches this branch with a real DivisionId — see DivisionNarrows.
        HashSet<string> allowed = _division.DivisionId is int divisionId
            ? ownAssignments
                .Where(a => a.DivisionId == divisionId)
                .Select(a => a.ProgramRefCode)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        return programs
            .Where(p => !ownAipOfficeIds.Contains(p.OfficeId) || allowed.Contains(p.RefCode))
            .ToList();
    }

    /// <summary>
    /// The config office id whose <see cref="ProgramDivision"/> rows
    /// <see cref="FilterPrograms"/> needs, or null when the division axis does not apply and no
    /// query should be issued.
    /// </summary>
    public int? OfficeIdForAssignments => DivisionNarrows ? _callerOfficeId : null;
}
