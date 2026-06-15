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
    string?  Username,
    string?  Email,
    /// <summary>"SuperAdmin" | "Admin" | "Staff" | "Observer" — triggers GroupId recalculation</summary>
    string?  Role,
    /// <summary>"Admin" | "Planning" | "RM" | "MIS" | "SPD" — triggers GroupId recalculation</summary>
    string?  Division,
    /// <summary>Explicit group override. When null, GroupId is auto-assigned from Role + Division/office.</summary>
    Guid?    GroupId,
    string?  Position,
    string?  ContactNo,
    bool?    OverrideCanAccessInventory,
    bool?    OverrideCanAccessReports,
    bool?    OverrideCanManageUsers,
    bool?    OverrideCanManageResourceLinks,
    // ── v1.1 (RAL-81) — added with defaults so existing callers stay valid ──
    /// <summary>
    /// Provincial office id (offices.id). Full-replacement, like the override flags:
    ///   null           → no office (PPDO-internal user)
    ///   positive value → non-PPDO office user (Division is cleared, Office User Default group applies)
    /// The form always resubmits the user's current value, so "unchanged" = resend the same value.
    /// </summary>
    int?     OfficeId                        = null,
    bool?    OverrideCanAccessBudgetPlanning = null,
    bool?    OverrideCanUploadAip            = null,
    bool?    OverrideCanManageConfig         = null);
