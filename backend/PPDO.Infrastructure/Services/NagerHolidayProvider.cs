using System.Net.Http.Json;
using PPDO.Application.DTOs.Dashboard;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Fetches PH public holidays from Nager.Date (https://date.nager.at).
/// Falls back to a hardcoded static list for 2026–2027 when the API is unreachable.
/// Results are cached in-memory per year for the lifetime of the host process.
/// </summary>
public sealed class NagerHolidayProvider : IHolidayProvider
{
    private readonly HttpClient _http;

    // In-memory cache: year → holiday list.
    private static readonly Dictionary<int, IReadOnlyList<CalendarEventDto>> _cache = new();
    private static readonly SemaphoreSlim _lock = new(1, 1);

    public NagerHolidayProvider(HttpClient http)
    {
        _http = http;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CalendarEventDto>> GetPhHolidaysAsync(
        int year,
        CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_cache.TryGetValue(year, out IReadOnlyList<CalendarEventDto>? cached))
                return cached;

            // Prefer static data for known years — no external HTTP call needed.
            IReadOnlyList<CalendarEventDto> staticData = GetStaticFallback(year);
            IReadOnlyList<CalendarEventDto> result = staticData.Count > 0
                ? staticData
                : await FetchFromNagerAsync(year, cancellationToken) ?? [];

            _cache[year] = result;
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    // ── Nager.Date HTTP call ───────────────────────────────────────────────────

    private async Task<IReadOnlyList<CalendarEventDto>?> FetchFromNagerAsync(
        int year, CancellationToken cancellationToken)
    {
        try
        {
            NagerHoliday[]? response = await _http.GetFromJsonAsync<NagerHoliday[]>(
                $"https://date.nager.at/api/v3/PublicHolidays/{year}/PH",
                cancellationToken);

            if (response is null) return null;

            return response.Select(h => new CalendarEventDto(
                null,
                h.Name,
                h.LocalName != h.Name ? h.LocalName : null,
                DateTime.SpecifyKind(h.Date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
                null,
                true,
                "Holiday",
                "Nager.Date",
                null,
                null)).ToList();
        }
        catch
        {
            return null;
        }
    }

    // ── Static fallback 2026–2027 ─────────────────────────────────────────────

    private static IReadOnlyList<CalendarEventDto> GetStaticFallback(int year)
    {
        List<(string date, string name)> holidays = year switch
        {
            2026 => Holidays2026,
            2027 => Holidays2027,
            _    => [],
        };

        return holidays.Select(h => new CalendarEventDto(
            null,
            h.name,
            null,
            DateTime.SpecifyKind(DateOnly.ParseExact(h.date, "yyyy-MM-dd").ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            null,
            true,
            "Holiday",
            "Static",
            null,
            null)).ToList();
    }

    // ── PH Public Holidays 2026 ───────────────────────────────────────────────
    // Source: Philippine Proclamation / RA 9492.
    // Special non-working days are marked "(Special NWD)".

    private static readonly List<(string, string)> Holidays2026 =
    [
        ("2026-01-01", "New Year's Day"),
        ("2026-01-02", "Special NWD — New Year Holiday"),
        ("2026-02-25", "EDSA People Power Revolution Anniversary"),
        ("2026-03-19", "Araw ng Kagitingan (Day of Valor)"),    // Maundy Thursday
        ("2026-03-20", "Good Friday"),
        ("2026-03-21", "Black Saturday (Special NWD)"),
        ("2026-04-09", "Araw ng Kagitingan (Day of Valor)"),
        ("2026-05-01", "Labor Day"),
        ("2026-06-12", "Independence Day"),
        ("2026-08-21", "Ninoy Aquino Day"),
        ("2026-08-31", "National Heroes Day"),
        ("2026-11-01", "All Saints' Day"),
        ("2026-11-02", "All Souls' Day (Special NWD)"),
        ("2026-11-30", "Bonifacio Day"),
        ("2026-12-08", "Feast of the Immaculate Conception (Special NWD)"),
        ("2026-12-24", "Christmas Eve (Special NWD)"),
        ("2026-12-25", "Christmas Day"),
        ("2026-12-30", "Rizal Day"),
        ("2026-12-31", "New Year's Eve (Special NWD)"),
    ];

    // ── PH Public Holidays 2027 ───────────────────────────────────────────────

    private static readonly List<(string, string)> Holidays2027 =
    [
        ("2027-01-01", "New Year's Day"),
        ("2027-02-25", "EDSA People Power Revolution Anniversary"),
        ("2027-04-01", "Maundy Thursday"),
        ("2027-04-02", "Good Friday"),
        ("2027-04-03", "Black Saturday (Special NWD)"),
        ("2027-04-09", "Araw ng Kagitingan (Day of Valor)"),
        ("2027-05-01", "Labor Day"),
        ("2027-06-12", "Independence Day"),
        ("2027-08-21", "Ninoy Aquino Day"),
        ("2027-08-30", "National Heroes Day"),
        ("2027-11-01", "All Saints' Day"),
        ("2027-11-02", "All Souls' Day (Special NWD)"),
        ("2027-11-30", "Bonifacio Day"),
        ("2027-12-08", "Feast of the Immaculate Conception (Special NWD)"),
        ("2027-12-24", "Christmas Eve (Special NWD)"),
        ("2027-12-25", "Christmas Day"),
        ("2027-12-30", "Rizal Day"),
        ("2027-12-31", "New Year's Eve (Special NWD)"),
    ];

    // ── Nager.Date response shape ─────────────────────────────────────────────

    private sealed record NagerHoliday(DateOnly Date, string Name, string LocalName);
}
