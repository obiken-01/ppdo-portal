using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Resolved office scope for a Budget Planning query (RAL-228; discriminator changed by
/// DECISION F, RAL-258).
///
/// <see cref="Office.IsHostOffice"/> is the cross-office-authority discriminator:
///   <see cref="SeeAll"/>  — caller in the host office: no office filter.
///   office id value       — any other office user: scoped to their own office.
///   <see cref="NoOffice"/> — caller with no office at all: scoped to nothing.
///
/// ⚠️ <b>The rule that changed — read this before "restoring" the old one.</b> Until DECISION F a
/// null <see cref="User.OfficeId"/> positively meant "PPDO-internal, sees everything", the inverse
/// of <see cref="DivisionScope"/> where null means "unassigned, sees nothing". Two mechanisms
/// described PPDO and nothing kept them in agreement, so cross-office authority now comes from the
/// office row's flag. That frees null to mean here what it means everywhere else: unassigned, and
/// therefore scoped to nothing. A user in that state has an incomplete record, not a privileged one.
///
/// The flag is read off the <see cref="User.Office"/> navigation property, so a query that forgets
/// <c>.Include(u =&gt; u.Office)</c> sees <c>false</c> and the caller is scoped to their own office.
/// That is deliberate: forgetting the include degrades to MORE restrictive, never to full access.
///
/// Office wins over role, unchanged. The SuperAdmin/Admin bypass in <c>PermissionService</c>
/// governs FEATURE flags, not data scope — an admin account deliberately tied to a guest office
/// stays scoped to it. (Pinned by
/// <c>OfficeScopeTests.Resolve_AdminOrAboveInANonHostOffice_IsStillScopedToThatOffice</c>.)
///
/// Prefer <see cref="Clamp"/> over validate-and-reject for caller-supplied office ids: silently
/// substituting the caller's own office leaves no error path to get wrong, and no way for a client
/// to probe for other offices' ids by watching which ones 403. Use <see cref="Permits"/> only when
/// the record's owning office is already known and a 403 is the correct answer.
/// </summary>
public readonly struct OfficeScope
{
    private OfficeScope(bool seeAll, int? officeId)
    {
        SeeAll   = seeAll;
        OfficeId = officeId;
    }

    /// <summary>No filter — a PPDO-internal caller may see every office.</summary>
    public bool SeeAll { get; }

    /// <summary>The single office id to scope to. Null when <see cref="SeeAll"/>.</summary>
    public int? OfficeId { get; }

    /// <summary>
    /// Office id used for a caller who has no office. Offices are IDENTITY(1,1), so nothing owns
    /// office 0 and every filter built from it returns empty — which is the point. It exists so
    /// <see cref="Clamp"/> never has to answer null for such a caller, because callers downstream
    /// read a null office id as "no filter — every office".
    /// </summary>
    public const int NoOffice = 0;

    /// <summary>Host-office caller — see every office.</summary>
    public static OfficeScope All { get; } = new(seeAll: true, officeId: null);

    /// <summary>Office user — scoped to one office.</summary>
    public static OfficeScope For(int officeId) => new(seeAll: false, officeId);

    /// <summary>
    /// Resolves the scope for a user. The host-office flag grants cross-office access; any other
    /// office scopes to itself; no office at all is scoped to <see cref="NoOffice"/> and sees
    /// nothing. See the type's remarks for why that last case is not full access any more.
    /// </summary>
    public static OfficeScope Resolve(User user)
        => IsHostOfficeUser(user) ? All : For(user.OfficeId ?? NoOffice);

    /// <summary>
    /// Whether <paramref name="user"/> belongs to the host office, and so holds cross-office
    /// authority (DECISION F, RAL-258). The one place this question is answered — call it rather
    /// than reading <c>OfficeId is null</c> or comparing office codes to <c>"PPDO"</c>.
    ///
    /// Requires <see cref="User.Office"/> to be loaded; returns false when it is not, which scopes
    /// the caller to their own office rather than granting the bypass.
    /// </summary>
    public static bool IsHostOfficeUser(User user) => user.Office?.IsHostOffice == true;

    /// <summary>
    /// Clamps a caller-supplied office id to what the caller is actually allowed to use.
    /// An office user always gets their own office, whatever they asked for; a PPDO caller
    /// gets the requested value unchanged (including null, meaning "all offices").
    /// </summary>
    public int? Clamp(int? requestedOfficeId)
        => SeeAll ? requestedOfficeId : OfficeId;

    /// <summary>
    /// Whether the caller may read a record owned by <paramref name="owningOfficeId"/>.
    /// A null owning office means the record belongs to no single office (e.g. LDIP's
    /// multi-office bulk uploads) — PPDO-only, never readable by an office user.
    /// </summary>
    public bool Permits(int? owningOfficeId)
        => SeeAll || (owningOfficeId is int id && id == OfficeId);
}
