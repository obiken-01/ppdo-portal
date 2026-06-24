namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/{id}</c>.
///
/// All fields are optional except where noted.
/// Null string fields leave the existing value unchanged.
/// Override flag fields (nullable bool) are always applied when present:
///   null  = clear override — user inherits from their Division flags
///   true  = explicitly grant
///   false = explicitly revoke
/// </summary>
public sealed record UpdateUserDto(
    string?  FullName,
    string?  Username,
    string?  Email,
    /// <summary>"SuperAdmin" | "Admin" | "Staff"</summary>
    string?  Role,
    /// <summary>Division id (divisions.id). Required for Staff; null for SuperAdmin/Admin.</summary>
    int?     DivisionId,
    string?  Position,
    string?  ContactNo,
    bool?    OverrideCanAccessInventory,
    bool?    OverrideCanAccessReports,
    bool?    OverrideCanManageUsers,
    bool?    OverrideCanManageResourceLinks,
    /// <summary>
    /// Provincial office id (offices.id). Full-replacement:
    ///   null           → no office (PPDO-internal user)
    ///   positive value → non-PPDO office user (its division must belong to that office)
    /// The form always resubmits the user's current value, so "unchanged" = resend the same value.
    /// </summary>
    int?     OfficeId                        = null,
    bool?    OverrideCanAccessBudgetPlanning = null,
    bool?    OverrideCanUploadAip            = null,
    bool?    OverrideCanManageConfig         = null,
    bool?    OverrideCanManageAllocation     = null);
