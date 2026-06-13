using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Application.Common;

/// <summary>
/// Resolved division scope for an inventory query. Introduced in RAL-81 because
/// <see cref="User.Division"/> became nullable in v1.1 (non-PPDO office users have
/// no division).
///
/// The three states are distinct on purpose:
///   <see cref="SeeAll"/>      — Admin/SuperAdmin: no division filter (all divisions).
///   <see cref="SeeNothing"/>  — Staff/Observer with a null division (an office user):
///                               must resolve to EMPTY results, never "all divisions".
///   division value            — Staff/Observer scoped to their own division.
///
/// ⚠️ Before this type, a null division meant the Admin "no filter" path. A Staff/Observer
/// with a null division would have leaked every division's data. Callers must branch on
/// <see cref="SeeNothing"/> first and return empty.
/// </summary>
public readonly struct DivisionScope
{
    private DivisionScope(bool seeAll, bool seeNothing, Division? division)
    {
        SeeAll     = seeAll;
        SeeNothing = seeNothing;
        Division   = division;
    }

    /// <summary>No filter — caller should query all divisions.</summary>
    public bool SeeAll { get; }

    /// <summary>Empty scope — caller must return no results (never "all divisions").</summary>
    public bool SeeNothing { get; }

    /// <summary>
    /// The single division to scope to, or null when <see cref="SeeAll"/> is true.
    /// Only meaningful when neither <see cref="SeeAll"/> nor <see cref="SeeNothing"/> applies,
    /// or when <see cref="SeeAll"/> is true (null = all).
    /// </summary>
    public Division? Division { get; }

    /// <summary>Admin/SuperAdmin — see every division.</summary>
    public static DivisionScope All { get; } = new(seeAll: true, seeNothing: false, division: null);

    /// <summary>Office user (Staff/Observer with no division) — see nothing.</summary>
    public static DivisionScope Nothing { get; } = new(seeAll: false, seeNothing: true, division: null);

    /// <summary>Staff/Observer scoped to a single division.</summary>
    public static DivisionScope For(Division division) => new(seeAll: false, seeNothing: false, division);

    /// <summary>
    /// Resolves the scope for a user:
    ///   SuperAdmin/Admin                 → <see cref="All"/>
    ///   Staff/Observer with a division   → <see cref="For"/>(division)
    ///   Staff/Observer with null division→ <see cref="Nothing"/>  (office users)
    /// </summary>
    public static DivisionScope Resolve(User user)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return All;

        return user.Division is Division division
            ? For(division)
            : Nothing;
    }
}
