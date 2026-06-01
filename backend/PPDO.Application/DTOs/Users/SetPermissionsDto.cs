namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/{id}/permissions</c> (SuperAdmin only).
///
/// This is a full replacement — all four override fields must be supplied.
/// <list type="bullet">
///   <item><c>null</c> — clear the override; user inherits the flag from their PermissionGroup.</item>
///   <item><c>true</c>  — explicitly grant access, overriding the group flag.</item>
///   <item><c>false</c> — explicitly revoke access, overriding the group flag.</item>
/// </list>
/// </summary>
public sealed class SetPermissionsDto
{
    public bool? OverrideCanAccessInventory { get; init; }
    public bool? OverrideCanAccessReports { get; init; }
    /// <summary>true is rejected for Observer users — Observer can never manage users.</summary>
    public bool? OverrideCanManageUsers { get; init; }
    /// <summary>true is rejected for Observer users — Observer can never manage resource links.</summary>
    public bool? OverrideCanManageResourceLinks { get; init; }
}
