namespace PPDO.Domain.Enums;

/// <summary>
/// Role assigned to a PPDO portal user. Controls which permission checks apply.
/// SuperAdmin and Admin always have full feature access — permission group flags are ignored for them.
/// Staff and Observer resolve permissions from their PermissionGroup plus individual overrides.
/// </summary>
public enum UserRole
{
    /// <summary>
    /// Developer / MIS staff. Bypasses ALL permission checks — full access to everything,
    /// all divisions, including user management of Admins.
    /// </summary>
    SuperAdmin = 0,

    /// <summary>
    /// Division head. Gets all feature permissions by default — group/override flags ignored.
    /// Can manage Staff and Observer accounts only (not other Admins or SuperAdmins).
    /// </summary>
    Admin = 1,

    /// <summary>
    /// Regular PPDO employee. Access determined by PermissionGroup flags + individual overrides.
    /// Write actions are scoped to own Division.
    /// </summary>
    Staff = 2,

    /// <summary>
    /// Read-only user (e.g. provincial administrator). Access determined by group flags + overrides.
    /// Can never create, edit, or delete — observer only.
    /// </summary>
    Observer = 3,
}
