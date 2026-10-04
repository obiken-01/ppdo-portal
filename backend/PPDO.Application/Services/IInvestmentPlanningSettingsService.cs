using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// The province-wide Investment Planning settings (PPDO-136) — today, the default fiscal year.
/// Callers gate on <c>IPermissionService.CanManageInvestmentPlanningSettingsAsync</c>; this
/// service does not re-check it.
/// </summary>
public interface IInvestmentPlanningSettingsService
{
    Task<DefaultFiscalYearDto> GetDefaultFiscalYearAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets (or, with <c>null</c>, clears) the default fiscal year. Rejects a year outside
    /// 2020 … current Manila year + 3. Saving the value already stored is a no-op: no write,
    /// no audit row.
    /// </summary>
    Task<ServiceResult<DefaultFiscalYearDto>> UpdateDefaultFiscalYearAsync(
        int? defaultFiscalYear, Guid actorId, CancellationToken cancellationToken = default);

    /// <summary>The investment proposal signatory defaults (PPDO-155, decision 25). All null when unset.</summary>
    Task<SignatoryDefaultsDto> GetSignatoryDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the four signatory defaults. Values are trimmed; a blank one clears it. Rejects any
    /// value over 200 characters. As with the fiscal year, an unchanged save writes nothing and
    /// logs no audit row.
    /// </summary>
    Task<ServiceResult<SignatoryDefaultsDto>> UpdateSignatoryDefaultsAsync(
        UpdateSignatoryDefaultsDto dto, Guid actorId, CancellationToken cancellationToken = default);
}
