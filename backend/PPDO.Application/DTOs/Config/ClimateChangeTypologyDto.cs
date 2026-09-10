namespace PPDO.Application.DTOs.Config;

/// <summary>Read model for a CCET climate-change typology code (RAL-247).</summary>
public sealed record ClimateChangeTypologyDto(
    int     Id,
    string  Code,
    string  Name,
    string  Category,
    string? Description,
    bool    IsActive);

/// <summary>
/// Create/update body. <c>Code</c> is the unique key.
/// <c>Category</c> is supplied rather than derived so a code that does not follow the CCET
/// A/M letter convention can still be filed correctly by hand.
/// </summary>
public sealed record UpsertClimateChangeTypologyDto(
    string  Code,
    string  Name,
    string  Category,
    string? Description = null,
    bool    IsActive    = true);
