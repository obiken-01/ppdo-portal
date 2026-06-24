using PPDO.Domain.Entities;
using PPDO.Domain.Enums;

namespace PPDO.Application.Common;

/// <summary>
/// Resolved division scope for an inventory query (v1.2 — RAL-97: division is now a
/// configurable FK <see cref="User.DivisionId"/>, no longer an enum).
///
/// The three states are distinct on purpose:
///   <see cref="SeeAll"/>      — Admin/SuperAdmin: no division filter (all divisions).
///   <see cref="SeeNothing"/>  — Staff with a null division id: must resolve to EMPTY
///                               results, never "all divisions".
///   division id value         — Staff scoped to their own division.
///
/// ⚠️ A Staff user with a null division id must branch on <see cref="SeeNothing"/> first and
/// return empty — never the open "all divisions" path.
/// </summary>
public readonly struct DivisionScope
{
    private DivisionScope(bool seeAll, bool seeNothing, int? divisionId)
    {
        SeeAll     = seeAll;
        SeeNothing = seeNothing;
        DivisionId = divisionId;
    }

    /// <summary>No filter — caller should query all divisions.</summary>
    public bool SeeAll { get; }

    /// <summary>Empty scope — caller must return no results (never "all divisions").</summary>
    public bool SeeNothing { get; }

    /// <summary>
    /// The single division id to scope to. Null when <see cref="SeeAll"/> or <see cref="SeeNothing"/>.
    /// </summary>
    public int? DivisionId { get; }

    /// <summary>Admin/SuperAdmin — see every division.</summary>
    public static DivisionScope All { get; } = new(seeAll: true, seeNothing: false, divisionId: null);

    /// <summary>Staff with no division id — see nothing.</summary>
    public static DivisionScope Nothing { get; } = new(seeAll: false, seeNothing: true, divisionId: null);

    /// <summary>Staff scoped to a single division.</summary>
    public static DivisionScope For(int divisionId) => new(seeAll: false, seeNothing: false, divisionId);

    /// <summary>
    /// Resolves the scope for a user:
    ///   SuperAdmin/Admin              → <see cref="All"/>
    ///   Staff with a division id      → <see cref="For"/>(id)
    ///   Staff with null division id   → <see cref="Nothing"/>
    /// </summary>
    public static DivisionScope Resolve(User user)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return All;

        return user.DivisionId is int divisionId
            ? For(divisionId)
            : Nothing;
    }
}
