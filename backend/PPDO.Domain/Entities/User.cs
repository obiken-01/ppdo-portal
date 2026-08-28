using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// A PPDO staff member who can log in to the portal.
/// Password is hashed via BCrypt (managed in Infrastructure/AuthService).
/// Permissions are resolved at runtime: Role → Group flags → Individual overrides.
/// </summary>
public sealed class User
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; set; }

    /// <summary>Full legal name. Max 100 characters.</summary>
    public string FullName { get; set; } = string.Empty;

    /// <summary>Login username. Must be unique. Max 50 characters.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Email address — optional, must be unique when set. Max 256 characters.</summary>
    public string? Email { get; set; }

    /// <summary>BCrypt password hash. Never store or log the plain-text password.</summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>Determines base permission behaviour. See UserRole XML docs for per-role rules.</summary>
    public UserRole Role { get; set; }

    /// <summary>
    /// FK to the configurable <see cref="Entities.Division"/> this user belongs to (v1.2 — RAL-97).
    /// Replaces the former Division enum AND GroupId: the division carries both the user's
    /// data scope AND their default feature flags.
    ///
    /// Null for SuperAdmin/Admin (they bypass/default all flags).
    /// ⚠️ A null DivisionId on a Staff user must resolve to an EMPTY inventory scope,
    /// never "all divisions" — see DivisionScope and the Inventory/Distribution guards.
    /// </summary>
    public int? DivisionId { get; set; }

    /// <summary>
    /// FK to the provincial office this user belongs to (<c>offices.id</c>). New in v1.1.
    /// The PPDO / non-PPDO discriminator:
    ///   null  → PPDO-internal user
    ///   set   → non-PPDO office user, scoped to that office's budget planning data only.
    /// </summary>
    public int? OfficeId { get; set; }

    /// <summary>Job title / position. Optional, max 100 characters.</summary>
    public string? Position { get; set; }

    /// <summary>Contact number. Optional.</summary>
    public string? ContactNo { get; set; }

    /// <summary>Soft-delete flag. Deactivated users cannot log in.</summary>
    public bool IsActive { get; set; } = true;

    // ── Individual permission overrides ───────────────────────────────────────
    // null  = inherit from the user's Division flags
    // true  = explicitly granted (overrides the division flag)
    // false = explicitly revoked (overrides the division flag)
    // SuperAdmin and Admin always have full access — these flags are ignored for them.

    /// <summary>
    /// Override for Inventory access. Null = use Group.CanAccessInventory.
    /// Ignored for SuperAdmin and Admin.
    /// </summary>
    public bool? OverrideCanAccessInventory { get; set; }

    /// <summary>
    /// Override for Reports access. Null = use Group.CanAccessReports.
    /// Ignored for SuperAdmin and Admin.
    /// </summary>
    public bool? OverrideCanAccessReports { get; set; }

    /// <summary>
    /// Override for User Management access. Null = use Group.CanManageUsers.
    /// Ignored for SuperAdmin and Admin. Observer can never have this set to true.
    /// </summary>
    public bool? OverrideCanManageUsers { get; set; }

    /// <summary>
    /// Override for Resource Links management access. Null = use Group.CanManageResourceLinks.
    /// Ignored for SuperAdmin and Admin. Observer can never have this set to true.
    /// Added in RAL-34.
    /// </summary>
    public bool? OverrideCanManageResourceLinks { get; set; }

    /// <summary>
    /// Override for Budget Planning access. Null = use Group.CanAccessBudgetPlanning.
    /// Ignored for SuperAdmin and Admin. Added in RAL-81 (v1.1).
    /// </summary>
    public bool? OverrideCanAccessBudgetPlanning { get; set; }

    /// <summary>
    /// Override for AIP upload/import. Null = use Group.CanUploadAip.
    /// Ignored for SuperAdmin and Admin. Observer and non-PPDO office users can never
    /// have this effectively granted (resolved in PermissionService). Added in RAL-81.
    /// </summary>
    public bool? OverrideCanUploadAip { get; set; }

    /// <summary>
    /// Override for Configuration management (Accounts, Offices, Funding Sources, Divisions).
    /// Null = use Division.CanManageConfig. Ignored for SuperAdmin and Admin.
    /// </summary>
    public bool? OverrideCanManageConfig { get; set; }

    /// <summary>
    /// Per-user grant for the Budget Allocation page (v1.2 — RAL-97). Unlike the other
    /// flags this is NOT a division flag: it is assigned to a specific finance-officer user
    /// regardless of role/division. Resolution: SuperAdmin → true; everyone else →
    /// <c>OverrideCanManagePpdoAllocation ?? false</c> (Admin is NOT auto-granted this).
    /// </summary>
    public bool? OverrideCanManagePpdoAllocation { get; set; }

    /// <summary>
    /// Per-user grant for setting an office's budget ceiling (v1.8.0 — RAL-243). Held by the
    /// Provincial Budget Office finance officer, who sets the ceiling for EVERY office —
    /// a different authority from <see cref="OverrideCanManagePpdoAllocation"/>, which only
    /// splits PPDO's own ceiling across its divisions. Like that flag this is NOT a division
    /// flag. Resolution: SuperAdmin → true; everyone else →
    /// <c>OverrideCanManagePboCeiling ?? false</c> (Admin is NOT auto-granted this).
    /// </summary>
    public bool? OverrideCanManagePboCeiling { get; set; }

    // ── Password reset (RAL-253) ────────────────────────────────────────────────

    /// <summary>
    /// The recovery question this user has chosen. Null until they complete the one-time setup
    /// screen (RAL-266) — self-service reset (RAL-265) is unavailable until this is set.
    /// </summary>
    public RecoveryQuestion? RecoveryQuestionKey { get; set; }

    /// <summary>
    /// BCrypt hash of the normalized recovery answer (see RecoveryAnswerNormalizer in
    /// PPDO.Application.Common). This IS a credential — never expose it, even to an admin, or
    /// self-service reset degrades into an admin-mediated reset with extra steps.
    /// </summary>
    public string? RecoveryAnswerHash { get; set; }

    /// <summary>
    /// True after any reset — self-service or admin-initiated — forcing a password change at next
    /// login before the user can reach anything else.
    /// </summary>
    public bool MustChangePassword { get; set; }

    /// <summary>
    /// Failed recovery-answer attempts within the current window. Reset to 0 on a successful
    /// verify. Paired with <see cref="RecoveryFirstAttemptAt"/> to enforce "5 failures in an hour".
    /// </summary>
    public int RecoveryAttemptCount { get; set; }

    /// <summary>
    /// UTC timestamp of the first failed attempt in the current window. Null when
    /// <see cref="RecoveryAttemptCount"/> is 0. The account is locked while
    /// <c>RecoveryAttemptCount >= 5 &amp;&amp; now &lt; RecoveryFirstAttemptAt + 1 hour</c>.
    /// </summary>
    public DateTime? RecoveryFirstAttemptAt { get; set; }

    /// <summary>
    /// UTC timestamp of the most recent password reset — self-service (RAL-265) or
    /// admin-initiated (RAL-254). Null if this account has never been reset. Paired with
    /// <see cref="PasswordResetAcknowledgedAt"/> to decide whether the "your password was
    /// reset" notice (RAL-267) is still owed to the user. A routine self-service password
    /// change via <c>ChangePasswordAsync</c> does NOT touch this — that's a voluntary change,
    /// not a reset someone else could have triggered.
    /// </summary>
    public DateTime? LastPasswordResetAt { get; set; }

    /// <summary>
    /// UTC timestamp the user last dismissed the reset notice (RAL-267). The notice is owed
    /// again whenever <see cref="LastPasswordResetAt"/> is set and is null or newer than this.
    /// </summary>
    public DateTime? PasswordResetAcknowledgedAt { get; set; }

    // ── Preferences ───────────────────────────────────────────────────────────

    /// <summary>
    /// This user's preferred landing page (RAL-251). Null = no preference; the resolver falls
    /// through to their division, then their office, then the first page they can actually reach.
    /// </summary>
    public LandingPage? LandingPage { get; set; }

    // ── Refresh token (JWT rotation) ─────────────────────────────────────────

    /// <summary>
    /// Opaque random token used to obtain a new access token when the current one expires.
    /// Stored as a BCrypt-free base64 string (64 random bytes, 88-char base64).
    /// Null when the user is not logged in or has logged out.
    /// Cleared on logout and rotated on every successful refresh.
    /// </summary>
    public string? RefreshToken { get; set; }

    /// <summary>
    /// UTC expiry for the refresh token. Null when RefreshToken is null.
    /// Server rejects refresh attempts past this timestamp.
    /// </summary>
    public DateTime? RefreshTokenExpiry { get; set; }

    // ── Audit ─────────────────────────────────────────────────────────────────

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The division this user belongs to. Null for SuperAdmin/Admin. Carries feature flags.</summary>
    public Division? Division { get; set; }

    /// <summary>The provincial office this user belongs to. Null for PPDO-internal users.</summary>
    public Office? Office { get; set; }
}
