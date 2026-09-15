using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Records one call against <c>/api/external/v1</c> (v1.8.0 — PPDO-13, build spec §2 decision 9,
/// §3.1). Separate from <see cref="IAuditService"/> — see <see cref="PartnerApiRequest"/> for why.
/// </summary>
public interface IPartnerApiRequestLogger
{
    /// <summary>
    /// Writes one <see cref="PartnerApiRequest"/> row and stamps
    /// <see cref="PartnerApiKey.LastUsedAt"/> on <paramref name="key"/>, in a single save. Call
    /// only for a request that reached a <c>200</c>, <c>400</c> or <c>403</c> outcome — build spec
    /// §3.1 is explicit that <c>401</c> (no authenticated key to attribute it to) and <c>429</c>
    /// (a flood, not a distinct call) write nothing.
    /// </summary>
    /// <param name="key">The authenticated key this call was made with.</param>
    /// <param name="route">e.g. <c>"aip"</c>, <c>"aip/fiscal-years"</c>.</param>
    /// <param name="officeCode">The <c>officeCode</c> query parameter as requested, or null for a
    /// whole-year call.</param>
    /// <param name="fiscalYear">The <c>fiscalYear</c> query parameter, when the route takes one.</param>
    /// <param name="statusCode">The HTTP status this call is about to return.</param>
    Task LogAsync(
        PartnerApiKey key,
        string route,
        string? officeCode,
        int? fiscalYear,
        int statusCode,
        CancellationToken cancellationToken = default);
}
