namespace PPDO.Application.DTOs.Config;

/// <summary>
/// Configurable division (v1.2 — RAL-97). Returned by GET /api/config/divisions.
/// Carries the office scope, identity, and the feature flags used in permission resolution.
/// </summary>
public sealed record DivisionDto(
    int Id,
    int OfficeId,
    string? OfficeName,
    string? Code,
    string Name,
    bool IsActive,
    bool CanAccessInventory,
    bool CanAccessReports,
    bool CanManageUsers,
    bool CanManageResourceLinks,
    bool CanAccessBudgetPlanning,
    bool CanUploadAip,
    bool CanManageConfig);
