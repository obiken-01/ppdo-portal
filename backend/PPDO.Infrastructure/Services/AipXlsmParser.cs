using ClosedXML.Excel;
using PPDO.Application.Services;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// ClosedXML-based parser for the AIP XLSM file (RAL-64).
///
/// Sheet detection: sheet names that start with GENERAL_, SOCIAL_, ECONOMIC_, or OTHERS_
/// (year suffix is ignored so the parser works for any fiscal year).
///
/// Column layout (1-based, rows 1–13 are headers; data starts row 14):
///   A  = AIP ref code          (all levels)
///   B  = Office description    (level 5 = Office)
///   C  = Program description   (level 6 = Program)
///   D  = Project description   (level 7 = Project)
///   E  = Activity description  (level 8 = Activity)
///   F  = eSRE code
///   G  = Implementing office
///   H  = Start date
///   I  = End date / completion
///   J  = Expected outputs
///   K  = Funding source (text)
///   L  = PS
///   M  = MOOE
///   N  = CO
///   O  = Total
///   P  = CC Adaptation
///   Q  = CC Mitigation
///   R  = CC Typology Code
///
/// Hierarchy detection: segment count in the ref code (split by '-'):
///   5 segments = Office, 6 = Program, 7 = Project, 8 = Activity.
///
/// Multi-line rows: if col A is empty or "None" and the previous row was an
/// Activity, the col-E text is appended to the previous activity's name.
/// </summary>
public sealed class AipXlsmParser : IAipXlsmParser
{
    private static readonly Dictionary<string, string> SheetPrefixToSector = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GENERAL_"]  = "GENERAL",
        ["SOCIAL_"]   = "SOCIAL",
        ["ECONOMIC_"] = "ECONOMIC",
        ["OTHERS_"]   = "OTHERS",
    };

    /// <inheritdoc />
    public Dictionary<string, List<ParsedAipOffice>> Parse(Stream xlsmStream)
    {
        using XLWorkbook wb = new(xlsmStream);

        Dictionary<string, List<ParsedAipOffice>> result = new(StringComparer.OrdinalIgnoreCase);
        List<string> globalErrors = new();

        foreach (IXLWorksheet ws in wb.Worksheets)
        {
            string? sector = DetectSector(ws.Name);
            if (sector is null) continue;

            List<ParsedAipOffice> offices = ParseSheet(ws, sector);
            if (result.ContainsKey(sector))
                result[sector].AddRange(offices);
            else
                result[sector] = offices;
        }

        if (result.Count == 0)
            globalErrors.Add("No recognised sector sheets found (expected GENERAL_*, SOCIAL_*, ECONOMIC_*, OTHERS_*).");

        if (globalErrors.Count > 0)
            throw new AipParseException(globalErrors);

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

    private static List<ParsedAipOffice> ParseSheet(IXLWorksheet ws, string sector)
    {
        List<ParsedAipOffice> offices = new();

        ParsedAipOffice?  currentOffice  = null;
        ParsedAipProgram? currentProgram = null;
        ParsedAipProject? currentProject = null;
        ParsedAipActivity? lastActivity  = null;
        List<ParsedAipActivity>? currentActivityList = null;

        int lastRow = ws.LastRowUsed()?.RowNumber() ?? 0;

        for (int row = 14; row <= lastRow; row++)
        {
            string refCode = ws.Cell(row, 1).GetString().Trim();

            // Blank / "None" in col A: possible continuation of previous activity name.
            if (string.IsNullOrWhiteSpace(refCode) || refCode.Equals("None", StringComparison.OrdinalIgnoreCase))
            {
                if (lastActivity is not null && currentActivityList is not null)
                {
                    string extra = ws.Cell(row, 5).GetString().Trim();
                    if (!string.IsNullOrWhiteSpace(extra))
                    {
                        // Rebuild the activity record with the appended name.
                        int idx = currentActivityList.IndexOf(lastActivity);
                        lastActivity = lastActivity with { Name = lastActivity.Name + " " + extra };
                        currentActivityList[idx] = lastActivity;
                    }
                }
                continue;
            }

            int segments = CountSegments(refCode);

            switch (segments)
            {
                case 5: // Office level
                {
                    string name = ws.Cell(row, 2).GetString().Trim();
                    currentOffice = new ParsedAipOffice(refCode, name, sector, new List<ParsedAipProgram>());
                    offices.Add(currentOffice);
                    currentProgram = null;
                    currentProject = null;
                    lastActivity   = null;
                    currentActivityList = null;
                    break;
                }
                case 6: // Program level
                {
                    if (currentOffice is null) break;
                    string name = ws.Cell(row, 3).GetString().Trim();
                    currentProgram = new ParsedAipProgram(refCode, name, new List<ParsedAipProject>());
                    ((List<ParsedAipProgram>)currentOffice.Programs).Add(currentProgram);
                    currentProject = null;
                    lastActivity   = null;
                    currentActivityList = null;
                    break;
                }
                case 7: // Project level
                {
                    if (currentProgram is null) break;
                    string name = ws.Cell(row, 4).GetString().Trim();
                    currentProject = new ParsedAipProject(refCode, name, new List<ParsedAipActivity>());
                    ((List<ParsedAipProject>)currentProgram.Projects).Add(currentProject);
                    lastActivity   = null;
                    currentActivityList = (List<ParsedAipActivity>)currentProject.Activities;
                    break;
                }
                case 8: // Activity level
                {
                    if (currentProject is null) break;
                    currentActivityList = (List<ParsedAipActivity>)currentProject.Activities;
                    ParsedAipActivity activity = new(
                        RefCode:           refCode,
                        Name:              ws.Cell(row, 5).GetString().Trim(),
                        EsreCode:          NullIfBlank(ws.Cell(row, 6).GetString()),
                        ImplementingOffice:NullIfBlank(ws.Cell(row, 7).GetString()),
                        StartDate:         NullIfBlank(ws.Cell(row, 8).GetString()),
                        EndDate:           NullIfBlank(ws.Cell(row, 9).GetString()),
                        ExpectedOutputs:   NullIfBlank(ws.Cell(row, 10).GetString()),
                        FundingSourceRaw:  NullIfBlank(ws.Cell(row, 11).GetString()),
                        Ps:                ParseDecimal(ws.Cell(row, 12)),
                        Mooe:              ParseDecimal(ws.Cell(row, 13)),
                        Co:                ParseDecimal(ws.Cell(row, 14)),
                        Total:             ParseDecimal(ws.Cell(row, 15)),
                        CcAdaptation:      ParseDecimal(ws.Cell(row, 16)),
                        CcMitigation:      ParseDecimal(ws.Cell(row, 17)),
                        CcTypologyCode:    NullIfBlank(ws.Cell(row, 18).GetString()));
                    currentActivityList.Add(activity);
                    lastActivity = activity;
                    break;
                }
                // Other segment counts are skipped (totals rows, etc.)
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
