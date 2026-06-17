namespace PPDO.Application.DTOs.Config;

/// <summary>
/// Read model for a provincial office (config table <c>offices</c>).
/// Returned by <c>GET /api/config/offices</c>.
///
/// RAL-81 ships only this read shape so the User Management office dropdown works.
/// Full config CRUD / CSV upload is RAL-70.
/// </summary>
public sealed record OfficeDto(
    int     Id,
    string  OfficeCode,
    string  OfficeName,
    string? OfficeRefCode,
    bool    IsActive);
