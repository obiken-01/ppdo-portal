using PPDO.Domain.Enums;

namespace PPDO.Application.Common;

/// <summary>
/// Parses the recovery-question enum name carried over the wire (RAL-266).
///
/// The API exchanges the name rather than the integer — "BirthTown", not 1 — matching how
/// <see cref="LandingPageName"/> already handles <c>LandingPage</c>. Unlike a landing-page
/// preference, a recovery question is required once the caller is setting one: blank is not
/// a valid value here.
/// </summary>
public static class RecoveryQuestionName
{
    /// <summary>Human-readable list of accepted values, for error messages.</summary>
    public static string ValidValues => string.Join(", ", Enum.GetNames<RecoveryQuestion>());

    /// <summary>Parses <paramref name="name"/>. False for blank, unknown, or out-of-range values.</summary>
    public static bool TryParse(string? name, out RecoveryQuestion question)
    {
        question = default;

        if (string.IsNullOrWhiteSpace(name))
            return false;

        return Enum.TryParse(name, ignoreCase: true, out question) && Enum.IsDefined(question);
    }
}
