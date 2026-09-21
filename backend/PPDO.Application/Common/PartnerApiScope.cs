using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// Authorizes a validated <see cref="PartnerApiKey"/> against a requested office code, for
/// <c>/api/external/v1</c> (v1.8.0 — PPDO-13, build spec §2 decision 8 and §3.1).
///
/// Deliberately pure, like <see cref="OfficeScope"/>: it takes the office the caller asked for —
/// already resolved by the caller via <c>IOfficeRepository.GetByCodeAsync</c>, or null when the
/// lookup found nothing — and returns a decision without touching a repository itself. Reused
/// unchanged by both <c>/aip</c> and <c>/aip/fiscal-years</c> (build spec §4.1), since both
/// endpoints apply exactly the same scope rule.
/// </summary>
public static class PartnerApiScope
{
    /// <summary>
    /// Decides whether <paramref name="key"/> may read <paramref name="requestedOfficeCode"/> —
    /// null meaning a whole-year request. Every branch below maps to one row of build spec §3.1:
    ///
    ///   officeCode omitted, all-offices key   → Ok
    ///   officeCode omitted, scoped key        → Forbidden — "request one office with officeCode"
    ///   officeCode given, no matching office  → BadRequest for an all-offices key ("unknown"),
    ///                                            Forbidden for a scoped key (an unknown code is
    ///                                            never in scope, so it reads the same as out of
    ///                                            scope rather than leaking which codes exist)
    ///   office exists but is inactive         → Forbidden, regardless of scope — a deactivated
    ///                                            office is never readable again, not merely absent
    ///   office in the key's own scope list     → Ok
    ///   office not in the key's scope list     → Forbidden
    /// </summary>
    /// <param name="key">The already-authenticated key (<see cref="IPartnerCredentialValidator"/>
    /// has already confirmed it is active).</param>
    /// <param name="requestedOfficeCode">The <c>officeCode</c> query parameter as sent, or null.</param>
    /// <param name="resolvedOffice">The office matching <paramref name="requestedOfficeCode"/>, or
    /// null when <paramref name="requestedOfficeCode"/> is null or matched nothing.</param>
    public static ServiceResult<bool> Authorize(
        PartnerApiKey key, string? requestedOfficeCode, Office? resolvedOffice)
    {
        if (requestedOfficeCode is null)
        {
            return key.AllOffices
                ? ServiceResult<bool>.Ok(true)
                : ServiceResult<bool>.Forbidden(
                    "This key can read only some offices; request one office with officeCode.");
        }

        if (resolvedOffice is null)
        {
            return key.AllOffices
                ? ServiceResult<bool>.BadRequest($"Unknown officeCode '{requestedOfficeCode}'.")
                : ServiceResult<bool>.Forbidden("Office not authorized for this key.");
        }

        if (!resolvedOffice.IsActive)
            return ServiceResult<bool>.Forbidden("Office not authorized for this key.");

        if (key.AllOffices)
            return ServiceResult<bool>.Ok(true);

        bool inScope = key.Offices.Any(o => o.OfficeId == resolvedOffice.Id);
        return inScope
            ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.Forbidden("Office not authorized for this key.");
    }
}
