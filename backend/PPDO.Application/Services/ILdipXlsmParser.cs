namespace PPDO.Application.Services;

/// <summary>
/// Parses an LDIP XLSX file (the 4 sector sheets only — General, Social, Economic,
/// Others; the workbook may also contain a "Program Description Form" and other
/// annex sheets, which are ignored) into an in-memory hierarchy.
/// Implementation lives in PPDO.Infrastructure (uses ClosedXML).
///
/// Unlike AIP (Office → Program → Project → Activity), the real LDIP source file is
/// only a 2-level hierarchy: every ref code in the reference workbook is either 5
/// segments (Office) or 6 segments (Program) — never 7 or 8. The Program row itself
/// carries all the activity-like detail (implementing office, schedule, funding
/// source, PS/MOOE/CO, CC amounts, alignment tags) directly.
/// </summary>
public interface ILdipXlsmParser
{
    /// <summary>
    /// Reads <paramref name="xlsxStream"/> and returns parsed offices grouped by
    /// sector key ("GENERAL" | "SOCIAL" | "ECONOMIC" | "OTHERS").
    /// Throws <see cref="LdipParseException"/> if the file cannot be read or
    /// contains none of the 4 recognised sector sheets.
    /// </summary>
    Dictionary<string, List<ParsedLdipOffice>> Parse(Stream xlsxStream);
}

// ── Parsed POCOs (not persisted — no IDs) ────────────────────────────────────

public record ParsedLdipOffice(string RefCode, string Name, string Sector, List<ParsedLdipProgram> Programs);

public record ParsedLdipProgram(
    string   RefCode,
    string   Name,
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
    string?  CcTypologyCode,
    string?  PdpRdp,
    string?  Sdgs,
    string?  SendaiFramework,
    string?  NdrrmPlan,
    string?  Nsp,
    string?  Pdpdfp);

/// <summary>Thrown when an uploaded LDIP file cannot be parsed.</summary>
public sealed class LdipParseException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public LdipParseException(IReadOnlyList<string> errors)
        : base($"LDIP file parse failed: {string.Join("; ", errors)}")
    {
        Errors = errors;
    }
}
