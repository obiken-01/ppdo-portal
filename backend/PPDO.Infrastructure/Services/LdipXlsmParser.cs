using ClosedXML.Excel;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// ClosedXML-based parser for the LDIP XLSX file (RAL-113).
///
/// Sheet detection: only the 4 sector sheets are read — General, Social, Economic,
/// Others (matched by prefix, case-insensitive, so a fiscal-year-suffixed variant
/// like "General_2027" still matches). Any other sheet in the workbook (e.g. the
/// "Program Description Form" or an "AIP 2027" annex) is ignored.
///
/// Column layout (1-based; header/title rows are skipped automatically because
/// their column A has no hyphen-segmented ref code):
///   A  = AIP ref code             (both levels)
///   B  = Office description       (level 5 = Office)
///   C  = Program description      (level 6 = Program — this IS the leaf level)
///   F  = Implementing office
///   G  = Start date (bare year, e.g. "2026" — not a month name like AIP)
///   H  = Completion date (bare year)
///   I  = Expected outputs
///   J  = Funding source (text)
///   K  = PS
///   L  = MOOE
///   M  = CO
///   N  = Total
///   O  = CC Adaptation amount
///   P  = CC Mitigation amount
///   Q  = CC Typology Code
///   R  = PDP/RDP alignment
///   S  = SDGs alignment
///   T  = Sendai Framework alignment
///   U  = NDRRM Plan alignment
///   V  = NSP alignment
///   W  = PDPDFP alignment
///
/// Hierarchy detection: segment count in the ref code (split by '-'). Unlike AIP,
/// LDIP has only 2 levels — 5 segments = Office, 6 segments = Program (the leaf;
/// no Project/Activity rows exist in the real source file). Any other segment
/// count is skipped (defensive — not expected in practice).
/// </summary>
public sealed class LdipXlsmParser : ILdipXlsmParser
{
    private static readonly Dictionary<string, string> SheetPrefixToSector = new(StringComparer.OrdinalIgnoreCase)
    {
        ["General"]  = "GENERAL",
        ["Social"]   = "SOCIAL",
        ["Economic"] = "ECONOMIC",
        ["Others"]   = "OTHERS",
    };

    /// <inheritdoc />
    public Dictionary<string, List<ParsedLdipOffice>> Parse(Stream xlsxStream)
    {
        using XLWorkbook wb = new(xlsxStream);

        Dictionary<string, List<ParsedLdipOffice>> result = new(StringComparer.OrdinalIgnoreCase);

        foreach (IXLWorksheet ws in wb.Worksheets)
        {
            string? sector = DetectSector(ws.Name);
            if (sector is null) continue;

            List<ParsedLdipOffice> offices = ParseSheet(ws, sector);
            if (result.TryGetValue(sector, out List<ParsedLdipOffice>? existing))
                existing.AddRange(offices);
            else
                result[sector] = offices;
        }

        if (result.Count == 0)
            throw new LdipParseException(
                ["No recognised sector sheets found (expected General, Social, Economic, Others)."]);

        return result;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string? DetectSector(string sheetName)
    {
        foreach (var (prefix, sector) in SheetPrefixToSector)
            if (sheetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return sector;
        return null;
    }

    private static List<ParsedLdipOffice> ParseSheet(IXLWorksheet ws, string sector)
    {
        List<ParsedLdipOffice> offices = new();
        ParsedLdipOffice? currentOffice = null;

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (int row = 1; row <= lastRow; row++)
        {
            string refCode = ws.Cell(row, 1).GetString().Trim();
            if (string.IsNullOrWhiteSpace(refCode)) continue;

            int segments = CountSegments(refCode);

            switch (segments)
            {
                case 5: // Office level
                {
                    string name = ws.Cell(row, 2).GetString().Trim();
                    currentOffice = new ParsedLdipOffice(refCode, name, sector, new List<ParsedLdipProgram>());
                    offices.Add(currentOffice);
                    break;
                }
                case 6: // Program level (the leaf — carries all activity-like detail)
                {
                    if (currentOffice is null) break;
                    ParsedLdipProgram program = new(
                        RefCode:            refCode,
                        Name:               ws.Cell(row, 3).GetString().Trim(),
                        ImplementingOffice: NullIfBlank(ws.Cell(row, 6).GetString()),
                        StartDate:          NullIfBlank(ws.Cell(row, 7).GetString()),
                        EndDate:            NullIfBlank(ws.Cell(row, 8).GetString()),
                        ExpectedOutputs:    NullIfBlank(ws.Cell(row, 9).GetString()),
                        FundingSourceRaw:   NullIfBlank(ws.Cell(row, 10).GetString()),
                        Ps:                 ParseDecimal(ws.Cell(row, 11)),
                        Mooe:               ParseDecimal(ws.Cell(row, 12)),
                        Co:                 ParseDecimal(ws.Cell(row, 13)),
                        Total:              ParseDecimal(ws.Cell(row, 14)),
                        CcAdaptation:       ParseDecimal(ws.Cell(row, 15)),
                        CcMitigation:       ParseDecimal(ws.Cell(row, 16)),
                        CcTypologyCode:     NullIfBlank(ws.Cell(row, 17).GetString()),
                        PdpRdp:             NullIfBlank(ws.Cell(row, 18).GetString()),
                        Sdgs:               NullIfBlank(ws.Cell(row, 19).GetString()),
                        SendaiFramework:    NullIfBlank(ws.Cell(row, 20).GetString()),
                        NdrrmPlan:          NullIfBlank(ws.Cell(row, 21).GetString()),
                        Nsp:                NullIfBlank(ws.Cell(row, 22).GetString()),
                        Pdpdfp:             NullIfBlank(ws.Cell(row, 23).GetString()));
                    ((List<ParsedLdipProgram>)currentOffice.Programs).Add(program);
                    break;
                }
                // Other segment counts are skipped (not expected in real LDIP files).
            }
        }

        return offices;
    }

    private static int CountSegments(string refCode)
    {
        if (string.IsNullOrEmpty(refCode)) return 0;
        int count = 1;
        foreach (char c in refCode)
            if (c == '-') count++;
        return count;
    }

    private static string? NullIfBlank(string? value)
    {
        string t = (value ?? string.Empty).Trim();
        return t.Length == 0 ? null : t;
    }

    private static decimal? ParseDecimal(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
            return (decimal)cell.GetDouble();
        string raw = cell.GetString().Trim();
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out decimal v) ? v : null;
    }
}
