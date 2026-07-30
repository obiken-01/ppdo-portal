namespace PPDO.Application.Common;

/// <summary>
/// The 4 AIP sectors and the numeric prefix each contributes to an office-level (level-1)
/// AIP reference code — <c>{prefix}-000-1-{Office.OfficeRefCode}</c>. Prefixes are read off
/// the existing imported data (RAL-62 manual entry); the same physical Office can legitimately
/// have entries under more than one sector within one AipRecord (e.g. PPDO runs programs under
/// both GENERAL and SOCIAL), so sector is a separate user choice, never derived from the office.
/// </summary>
public static class AipSector
{
    public const string General  = "GENERAL";
    public const string Social   = "SOCIAL";
    public const string Economic = "ECONOMIC";
    public const string Others   = "OTHERS";

    public static readonly IReadOnlyDictionary<string, string> Prefixes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [General]  = "1000",
            [Social]   = "3000",
            [Economic] = "8000",
            [Others]   = "9000",
        };
}
