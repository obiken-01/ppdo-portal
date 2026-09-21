using PPDO.Application.DTOs.ExternalApi;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// Reads the released AIP for external partners — <c>docs/external-api/aip-response.schema.json</c>
/// draft 1.0.0 (v1.8.0 — PPDO-14). Pure read, no auth/scope decisions: the caller (PPDO-12's
/// Function) has already resolved <c>officeCode</c> to an <see cref="Office"/> (or confirmed the
/// request wants the whole year) and authorized it via <c>PartnerApiScope</c> before calling here.
/// </summary>
public interface IExternalAipReadService
{
    /// <summary>
    /// Returns the fiscal year's released AIP, or null when the year has no AIP at all (a valid
    /// <c>200</c> with <c>data: null</c>, not an error — build spec §3.1 "Year not opened"/"Legacy
    /// draft"). A year that exists but has nothing released yet returns a non-null result with
    /// empty <see cref="ExternalAipDto.Offices"/> and a filled
    /// <see cref="ExternalAipDto.PendingOffices"/>.
    /// </summary>
    /// <param name="fiscalYear">The requested fiscal year.</param>
    /// <param name="filterOffice">The office to scope to, already resolved from <c>officeCode</c>
    /// and authorized by the caller — or null for the whole fiscal year.</param>
    Task<ExternalAipDto?> GetAsync(
        int fiscalYear, Office? filterOffice, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fiscal years with at least one released office, newest first — <c>GET
    /// /api/external/v1/aip/fiscal-years</c>. Scoped to <paramref name="filterOffice"/> when given
    /// (years in which that one office is released), matching <see cref="GetAsync"/>'s scoping.
    /// </summary>
    Task<IReadOnlyList<int>> GetFiscalYearsAsync(
        Office? filterOffice, CancellationToken cancellationToken = default);
}
