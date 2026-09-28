using System.Text.Json.Serialization;

namespace PPDO.Application.DTOs.Config;

/// <summary>
/// The province-wide default fiscal year and who last set it (PPDO-136).
/// <see cref="DefaultFiscalYear"/> null = unset.
/// </summary>
public record DefaultFiscalYearDto(
    int?      DefaultFiscalYear,
    DateTime? UpdatedAt,
    string?   UpdatedByName);

/// <summary>
/// Request body for <c>PUT /api/config/investment-planning/default-fiscal-year</c>.
/// <c>null</c> clears the default.
///
/// ⚠️ <see cref="JsonRequiredAttribute"/> makes the property mandatory while still allowing an
/// explicit <c>null</c>. Without it, an empty <c>{}</c> body would bind to null and silently
/// <b>clear</b> the setting instead of being rejected as malformed.
/// </summary>
public record UpdateDefaultFiscalYearDto(
    [property: JsonRequired] int? DefaultFiscalYear);
