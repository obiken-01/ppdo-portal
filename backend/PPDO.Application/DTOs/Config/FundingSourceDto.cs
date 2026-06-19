namespace PPDO.Application.DTOs.Config;

/// <summary>Read model for a funding source.</summary>
public sealed record FundingSourceDto(
    int     Id,
    string  Code,
    string  Name,
    string? Description,
    string? Color,
    bool    IsActive);

/// <summary>Create/update body for a funding source. Code is the unique key.</summary>
public sealed record UpsertFundingSourceDto(
    string  Code,
    string  Name,
    string? Description,
    string? Color = null,
    bool    IsActive = true);
