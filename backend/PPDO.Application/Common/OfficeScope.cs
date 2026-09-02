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
    /// Resolves the scope for a caller on a <b>review READ path</b> (v1.8.0 — RAL-257).
    /// Identical to <see cref="Resolve"/>, except that a cross-office reviewer sees every office
    /// even when they belong to one themselves.
    ///
    /// <paramref name="canReviewAllOffices"/> must come from
    /// <c>IPermissionService.CanReviewAllOfficesAsync</c>. It is passed in rather than read off
    /// the user here because effective permission resolution belongs in <c>PermissionService</c>
    /// and this type is deliberately pure — see CLAUDE.md.
    ///
    /// <b>⚠️ Why this is a separate method rather than a branch inside <see cref="Resolve"/>.</b>
    /// <see cref="Resolve"/> feeds the write paths too, through <see cref="Clamp"/>. Teaching it
    /// this flag would silently promote a cross-office <i>reviewer</i> into a cross-office
    /// <i>editor</i> of every office's data — a much larger grant than RAL-257 asks for, arriving
    /// with no diff at any write site to notice it. Keeping the bypass on its own entry point
    /// means a write path can only acquire it by being deliberately changed to call this instead.
    /// Do not "simplify" the two back together.
    ///
    /// The reviewer's own office is intentionally ignored, not combined: a reviewer who sits in
    /// GSO reviews every office, not GSO's rows plus everyone else's. That is the case most
    /// likely to be got wrong, and it is pinned by test.
    /// </summary>
    public static OfficeScope ResolveForReview(User user, bool canReviewAllOffices)
        => canReviewAllOffices ? All : Resolve(user);

    /// <summary>
    /// Resolves the scope for a caller on the <b>allocation-setup surface</b> — ceilings and the
    /// office's allocation setup around them (v1.8.0 — PPDO-18).
    /// Identical to <see cref="Resolve"/>, except that a holder of <c>CanManagePboCeiling</c> is
    /// scoped to every office even when they belong to one themselves.
    ///
    /// <paramref name="canManagePboCeiling"/> must come from
    /// <c>IPermissionService.CanManagePboCeilingAsync</c> — passed in rather than read off the
    /// user here because effective permission resolution belongs in <c>PermissionService</c> and
    /// this type is deliberately pure. Same contract as <see cref="ResolveForReview"/>.
    ///
    /// <b>Why the grant reaches past the ceiling row itself.</b> RAL-243 gave the Provincial
    /// Budget Office authority to set a ceiling for ANY office. The Allocation page that exercises
    /// that authority loads the office's ceilings, its division split, its PPA assignments and its
    /// setup status together, so scoping only the ceiling read would leave the holder looking at a
    /// page that cannot render. The grant is therefore the office axis for that whole read
    /// surface. It is NOT authority over another office's internal division split — those writes
    /// stay on <see cref="Resolve"/>; see the warning below.
    ///
    /// <b>⚠️ Why this is a third entry point rather than a branch inside <see cref="Resolve"/>.</b>
    /// Exactly the reason given on <see cref="ResolveForReview"/>, and it applies with more force
    /// here because this grant legitimately covers reads AND the ceiling write. <see cref="Resolve"/>
    /// feeds every other write path through <see cref="Clamp"/>; teaching it this flag would
    /// silently promote a PBO ceiling officer into an editor of every office's division
    /// allocations and PPA assignments, arriving with no diff at any write site to notice it.
    /// Reusing <see cref="ResolveForReview"/> instead would be just as wrong in the other
    /// direction — it would hand a comment-only cross-office reviewer the ceiling write. The three
    /// resolvers answer three different questions; do not "simplify" them together.
    ///
    /// Pinned by <c>OfficeScopeTests.Resolve_IgnoresThePboCeilingGrant_SoAllocationWritesStayScoped</c>
    /// and <c>TheTwoBypasses_DoNotLeakIntoEachOther</c>.
    /// </summary>
    public static OfficeScope ResolveForCeiling(User user, bool canManagePboCeiling)
        => canManagePboCeiling ? All : Resolve(user);

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
