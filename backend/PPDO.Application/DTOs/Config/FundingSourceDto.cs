namespace PPDO.Application.DTOs.Config;

/// <summary>
/// Read model for a funding source.
///
/// <paramref name="OfficeId"/> null means the fund is province-wide and PPDO's (PPDO-109, D5);
/// <paramref name="IsShared"/> is that same fact stated for the UI, which renders a shared row
/// read-only for a department head. Both are sent because the client needs the boolean to decide
/// what to disable and the id to label whose fund it is.
/// </summary>
public sealed record FundingSourceDto(
    int     Id,
    string  Code,
    string  Name,
    string? Description,
    string? Color,
    bool    IsActive,
    string? Aliases    = null,
    int?    OfficeId   = null,
    string? OfficeCode = null,
    string? OfficeName = null)
{
    /// <summary>True for a province-wide fund — no owning office. Derived, never stored.</summary>
    public bool IsShared => OfficeId is null;
}

/// <summary>
/// Create/update body for a funding source. Code is the unique key.
///
/// ⚠️ <paramref name="OfficeId"/> is honoured only for a config manager, who may create a fund on
/// an office's behalf — and, since PPDO-128, move an existing one between shared (null) and
/// office-owned on UPDATE, so an update body must always carry the owner it wants. For a caller holding <c>CanManageOfficeSetup</c> alone the field is
/// OVERWRITTEN with their own office id in <c>ConfigFundingSourceFunctions</c> — a body that names
/// someone else's office is ignored, not rejected (PPDO-109).
/// </summary>
public sealed record UpsertFundingSourceDto(
    string  Code,
    string  Name,
    string? Description,
    string? Color    = null,
    bool    IsActive = true,
    string? Aliases  = null,
    int?    OfficeId = null);
