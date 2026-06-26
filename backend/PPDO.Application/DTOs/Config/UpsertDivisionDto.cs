namespace PPDO.Application.DTOs.Config;

/// <summary>
/// Create / update body for a configurable division (RAL-98).
/// Name is the upsert key within an office — it cannot be changed on edit.
/// Code is optional (some divisions have no official short code).
/// </summary>
public sealed record UpsertDivisionDto(
    int     OfficeId,
    string? Code,
    string  Name,
    bool    IsActive,
    bool    CanAccessBudgetPlanning,
    bool    CanAccessInventory,
    bool    CanAccessReports,
    bool    CanManageConfig,
    bool    CanUploadAip,
    bool    CanManageUsers,
    bool    CanManageResourceLinks);
