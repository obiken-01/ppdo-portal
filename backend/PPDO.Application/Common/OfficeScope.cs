using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// Resolved office scope for a Budget Planning query (v1.8.0 — RAL-228).
///
/// <see cref="User.OfficeId"/> is the PPDO / non-PPDO discriminator:
///   <see cref="SeeAll"/>  — PPDO-internal caller (OfficeId null): no office filter.
///   office id value       — non-PPDO office user: scoped to their own office.
///
/// ⚠️ <b>Deliberately asymmetric with <see cref="DivisionScope"/> — read this before changing it.</b>
/// DivisionScope has a third "SeeNothing" state because a Staff user with a null DivisionId is an
/// <i>unassigned</i> user who must resolve to EMPTY results. OfficeScope has no such state: a null
/// OfficeId is not "unassigned", it positively means <i>PPDO-internal</i>, which is the
/// full-access case. Treating null the same way in both types inverts the security check.
///
/// Office wins over role. The SuperAdmin/Admin bypass in <c>PermissionService</c> governs FEATURE
/// flags, not data scope — an admin account deliberately tied to an office stays scoped to it.
/// (Pinned by <c>OfficeScopeTests.Resolve_AdminOrAboveWithOfficeIdSet_IsStillScopedToThatOffice</c>.)
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

    /// <summary>PPDO-internal caller — see every office.</summary>
    public static OfficeScope All { get; } = new(seeAll: true, officeId: null);

    /// <summary>Office user — scoped to one office.</summary>
    public static OfficeScope For(int officeId) => new(seeAll: false, officeId);

    /// <summary>
    /// Resolves the scope for a user: an OfficeId means an office user scoped to it,
    /// null means a PPDO-internal user with full cross-office access.
    /// </summary>
    public static OfficeScope Resolve(User user)
        => user.OfficeId is int officeId ? For(officeId) : All;

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
