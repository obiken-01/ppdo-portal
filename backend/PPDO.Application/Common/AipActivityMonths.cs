namespace PPDO.Application.Common;

/// <summary>
/// The start and end month of an AIP activity (PPDO-289). The columns are free text and the FY 2027
/// rows show it: "Januray", "Jan", "2027", "Fund provided", and reversed pairs. The entry page
/// offers only these twelve names, so the FY 2028+ details save accepts only them, in order.
///
/// ⚠️ Mirrors <c>AIP_MONTHS</c> in <c>frontend/src/lib/aipConstants.ts</c>. Change both together.
/// </summary>
public static class AipActivityMonths
{
    public static readonly string[] Names =
    [
        "January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December",
    ];

    /// <summary>The month's canonical name, matched trimmed and case-insensitively; null when it is not a month.</summary>
    public static string? Canonical(string value)
        => Names.FirstOrDefault(n => n.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>1–12 for a month name, 0 for anything else.</summary>
    public static int Number(string? value)
        => value is null ? 0 : Array.IndexOf(Names, Canonical(value) ?? string.Empty) + 1;
}
