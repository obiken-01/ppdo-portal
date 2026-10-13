using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// The division axis of the WFP surface (v1.8.3 — PPDO-198). WFP is PPDO's own plan, and PPDO
/// separates it by division: a Staff encoder works on their own division's WFP only.
///
/// <b>This is only half the rule.</b> The office axis — WFP belongs to the host office, so a guest
/// office may not reach it at all — is <c>IPermissionService.CanAccessWfpAsync</c>, checked at the
/// endpoint gate. This type assumes that gate has already passed.
///
/// Every WFP path that names a record — by WFP id, by WFP activity id, by expenditure id, or by a
/// division id in a request body — must check <see cref="Allows"/> against that record's division
/// before reading or writing it. A record outside the scope answers NotFound, the same as a
/// missing id, so ids cannot be probed.
/// </summary>
public static class WfpDivisionScope
{
    /// <summary>
    /// Resolves the caller's division scope for WFP.
    ///
    ///   SuperAdmin / Admin                → <see cref="DivisionScope.All"/>
    ///   <c>CanManagePpdoAllocation</c>    → <see cref="DivisionScope.All"/> (finance sees every division)
    ///   Staff with a division             → <see cref="DivisionScope.For"/>(their division)
    ///   Staff with no division            → <see cref="DivisionScope.Nothing"/> — never "all"
    ///
    /// <paramref name="canManagePpdoAllocation"/> must come from
    /// <c>IPermissionService.CanManagePpdoAllocationAsync</c>; it is passed in so this type stays
    /// pure, the same contract as <see cref="OfficeScope.ResolveForReview"/>.
    /// </summary>
    public static DivisionScope Resolve(User caller, bool canManagePpdoAllocation)
        => canManagePpdoAllocation ? DivisionScope.All : DivisionScope.Resolve(caller);

    /// <summary>
    /// <see cref="Resolve"/>, reading the finance grant from <paramref name="permissions"/> — the one
    /// call the WFP endpoints make, so every handler resolves the scope the same way.
    /// </summary>
    public static async Task<DivisionScope> ResolveAsync(User caller, IPermissionService permissions)
        => Resolve(caller, await permissions.CanManagePpdoAllocationAsync(caller));

    /// <summary>
    /// Whether a WFP record in <paramref name="recordDivisionId"/> is inside <paramref name="scope"/>.
    /// A division-less (office-level) record is inside only an unrestricted scope.
    /// </summary>
    public static bool Allows(DivisionScope scope, int? recordDivisionId)
        => scope.SeeAll || (!scope.SeeNothing && recordDivisionId == scope.DivisionId);

    /// <summary>
    /// The division filter for a list or report query, where null means "every division".
    ///
    ///   Unrestricted scope   → <paramref name="requestedDivisionId"/> (optional narrowing)
    ///   Division-scoped      → the caller's own division, whatever they requested
    ///   Nothing              → returns false: the caller must answer empty, and must NOT run the
    ///                          query, because a null filter there would mean every division
    /// </summary>
    public static bool TryFilter(DivisionScope scope, int? requestedDivisionId, out int? divisionId)
    {
        divisionId = scope.SeeAll ? requestedDivisionId : scope.DivisionId;
        return !scope.SeeNothing;
    }
}
