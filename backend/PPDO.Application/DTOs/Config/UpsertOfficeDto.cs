namespace PPDO.Application.DTOs.Config;

/// <summary>Create/update body for an office. OfficeCode is the unique key.</summary>
public sealed record UpsertOfficeDto(
    string  OfficeCode,
    string  OfficeName,
    bool    IsActive = true,
    string? OfficeRefCode = null);
