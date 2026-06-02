using PPDO.Application.DTOs.Dashboard;

namespace PPDO.Application.Services;

/// <summary>
/// Provides PH public holidays for a given year.
/// The default implementation calls Nager.Date with a static fallback for 2026–2027.
/// Mockable for unit tests.
/// </summary>
public interface IHolidayProvider
{
    /// <summary>
    /// Returns all PH public holidays for <paramref name="year"/>.
    /// Never throws — returns an empty list on failure.
    /// </summary>
    Task<IReadOnlyList<CalendarEventDto>> GetPhHolidaysAsync(
        int year,
        CancellationToken cancellationToken = default);
}
