using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository for the single-row <see cref="InvestmentPlanningSettings"/> table (PPDO-136).
/// Not an <see cref="IRepository{T}"/>: there is nothing to list, add or delete — only the one
/// seeded row to read and change.
/// </summary>
public interface IInvestmentPlanningSettingsRepository
{
    /// <summary>
    /// The settings row, tracked for update, with <see cref="InvestmentPlanningSettings.UpdatedBy"/>
    /// loaded for the last-changed line. Null only if the migration's seed row is missing.
    /// </summary>
    Task<InvestmentPlanningSettings?> GetAsync(CancellationToken ct = default);

    /// <summary>
    /// Just the default fiscal year, as a single-column read. This is on the path of every
    /// Investment Planning page load (via the fiscal-year resolver), so it does not load the row.
    /// </summary>
    Task<int?> GetDefaultFiscalYearAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
