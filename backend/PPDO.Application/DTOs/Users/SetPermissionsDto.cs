namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/users/{id}/permissions</c> (SuperAdmin only).
///
/// This is a full replacement — all override fields must be supplied.
/// <list type="bullet">
///   <item><c>null</c> — clear the override; user inherits the flag from their Division.</item>
///   <item><c>true</c>  — explicitly grant access, overriding the division flag.</item>
///   <item><c>false</c> — explicitly revoke access, overriding the division flag.</item>
/// </list>
/// </summary>
public sealed class SetPermissionsDto
{
    public bool? OverrideCanAccessInventory { get; init; }
    public bool? OverrideCanAccessReports { get; init; }
    public bool? OverrideCanManageUsers { get; init; }
    public bool? OverrideCanManageResourceLinks { get; init; }
    public bool? OverrideCanAccessBudgetPlanning { get; init; }
    public bool? OverrideCanUploadAip { get; init; }
    public bool? OverrideCanManageConfig { get; init; }

    /// <summary>Per-user grant for the Budget Allocation page (v1.2 — RAL-97).</summary>
    public bool? OverrideCanManagePpdoAllocation { get; init; }

    /// <summary>Per-user grant for setting any office's budget ceiling (v1.8.0 — RAL-243).</summary>
    public bool? OverrideCanManagePboCeiling { get; init; }

    /// <summary>Per-user grant marking this user their office's budget-planning reviewer (v1.8.0 — RAL-244).</summary>
    public bool? OverrideCanReviewBudgetPlanning { get; init; }

    /// <summary>Per-user grant to review EVERY office's budget-planning work (v1.8.0 — RAL-257).</summary>
    public bool? OverrideCanReviewAllOffices { get; init; }
}
