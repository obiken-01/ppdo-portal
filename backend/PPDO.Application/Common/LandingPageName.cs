using PPDO.Domain.Enums;

namespace PPDO.Application.Common;

/// <summary>
/// Parses the landing-page enum name carried over the wire (RAL-262).
///
/// The API exchanges the name rather than the integer — "InventoryDashboard", not 2 — matching
/// how <c>Role</c> is already handled, and keeping the wire format readable and stable if the
/// enum's backing numbers ever shift.
/// </summary>
public static class LandingPageName
{
    /// <summary>Human-readable list of accepted values, for error messages.</summary>
    public static string ValidValues => string.Join(", ", Enum.GetNames<LandingPage>());

    /// <summary>
    /// Parses <paramref name="name"/>. Null or blank is valid and means "no preference",
    /// returning true with a null <paramref name="page"/>.
    /// </summary>
    /// <returns>False only when a non-blank value is not a known member.</returns>
    public static bool TryParse(string? name, out LandingPage? page)
    {
        page = null;

        if (string.IsNullOrWhiteSpace(name))
            return true;

        if (Enum.TryParse(name, ignoreCase: true, out LandingPage parsed) && Enum.IsDefined(parsed))
        {
            page = parsed;
            return true;
        }

        return false;
    }
}
