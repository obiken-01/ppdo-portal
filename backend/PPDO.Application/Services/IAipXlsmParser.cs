namespace PPDO.Application.Services;

/// <summary>
/// Parses an AIP XLSM file (all 4 sector sheets) into an in-memory hierarchy.
/// Implementation lives in PPDO.Infrastructure (uses ClosedXML).
/// </summary>
public interface IAipXlsmParser
{
    /// <summary>
    /// Reads <paramref name="xlsmStream"/> and returns parsed offices grouped by
    /// sector key ("GENERAL" | "SOCIAL" | "ECONOMIC" | "OTHERS").
    /// Throws <see cref="AipParseException"/> if the file cannot be read or
    /// contains no recognised sector sheets.
    /// </summary>
    Dictionary<string, List<ParsedAipOffice>> Parse(Stream xlsmStream);
}

// ── Parsed POCOs (not persisted — no IDs) ────────────────────────────────────

public record ParsedAipOffice(string RefCode, string Name, string Sector, List<ParsedAipProgram> Programs);

// RAL-108: a program/project row may carry its own line-item detail (e.g. the Provincial Legal
// Office's program "1000-000-1-01-011-004") — the same optional fields as ParsedAipActivity are
// mirrored here, all defaulting to null for the normal case of a program/project that only
// groups its children.
public record ParsedAipProgram(
    string   RefCode,
    string   Name,
    List<ParsedAipProject> Projects,
    string?  EsreCode = null,
    string?  ImplementingOffice = null,
    string?  StartDate = null,
    string?  EndDate = null,
    string?  ExpectedOutputs = null,
    string?  FundingSourceRaw = null,
    decimal? Ps = null,
    decimal? Mooe = null,
    decimal? Co = null,
    decimal? Total = null,
    decimal? CcAdaptation = null,
    decimal? CcMitigation = null,
    string?  CcTypologyCode = null);

public record ParsedAipProject(
    string   RefCode,
    string   Name,
    List<ParsedAipActivity> Activities,
    string?  EsreCode = null,
    string?  ImplementingOffice = null,
    string?  StartDate = null,
    string?  EndDate = null,
    string?  ExpectedOutputs = null,
    string?  FundingSourceRaw = null,
    decimal? Ps = null,
    decimal? Mooe = null,
    decimal? Co = null,
    decimal? Total = null,
    decimal? CcAdaptation = null,
    decimal? CcMitigation = null,
    string?  CcTypologyCode = null);

public record ParsedAipActivity(
    string   RefCode,
    string   Name,
    string?  EsreCode,
    string?  ImplementingOffice,
    string?  StartDate,
    string?  EndDate,
    string?  ExpectedOutputs,
    string?  FundingSourceRaw,
    decimal? Ps,
    decimal? Mooe,
    decimal? Co,
    decimal? Total,
    decimal? CcAdaptation,
    decimal? CcMitigation,
    string?  CcTypologyCode);

/// <summary>Thrown when an uploaded AIP XLSM file cannot be parsed.</summary>
public sealed class AipParseException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public AipParseException(IReadOnlyList<string> errors)
        : base($"AIP file parse failed: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }
}
