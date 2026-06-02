namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/{id}</c>.
///
/// All fields are optional except where noted.
/// Null string fields leave the existing value unchanged.
/// Override flag fields (nullable bool) are always applied when present:
///   null  = clear override — user inherits from their PermissionGroup
///   true  = explicitly grant
///   false = explicitly revoke
/// </summary>
public sealed record UpdateUserDto(
    string?  FullName,
    /// <summary>"SuperAdmin" | "Admin" | "Staff" | "Observer" — triggers GroupId recalculation</summary>
    string?  Role,
    /// <summary>"Admin" | "Planning" | "RM" | "MIS" | "SPD" — triggers GroupId recalculation</summary>
    string?  Division,
    /// <summary>Explicit group override. When null, GroupId is auto-assigned from Role + Division.</summary>
    Guid?    GroupId,
    string?  Position,
    string?  ContactNo,
    bool?    OverrideCanAccessInventory,
    bool?    OverrideCanAccessReports,
    bool?    OverrideCanManageUsers,
    bool?    OverrideCanManageResourceLinks);
