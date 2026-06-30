namespace PPDO.Domain.Enums;

/// <summary>
/// Role assigned to a PPDO portal user. Controls which permission checks apply.
/// SuperAdmin bypasses everything; Admin gets all feature flags by default EXCEPT special
/// per-user grants (e.g. CanManageAllocation). Staff resolve permissions from their
/// Division flags plus individual overrides.
///
/// The Observer role was retired in v1.2 (RAL-97) — read-only access is deferred.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Developer / MIS staff. Bypasses ALL permission checks — full access to everything,
    /// all divisions, including user management of Admins.
    /// </summary>
    SuperAdmin = 0,

    /// <summary>
    /// Division head. Gets all feature permissions by default EXCEPT special per-user grants
    /// (CanManageAllocation). Can manage Staff accounts only (not other Admins or SuperAdmins).
    /// </summary>
    Admin = 1,

    /// <summary>
    /// Regular PPDO or office employee. Access determined by their division's flags + individual
    /// overrides. Write/read scope is their own division (offices users: their office).
    /// </summary>
    Staff = 2,
}
