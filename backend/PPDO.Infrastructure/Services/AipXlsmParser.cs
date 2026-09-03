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
/// Hierarchy detection (RAL-238): a row's level is taken from WHICH description column
/// holds its text — B = Office, C = Program, D = Project, E = Activity. Columns B:E are
/// merged as B8:E9 in the source form and act as indent columns, so the indentation is the
/// author's actual statement of intent.
///
/// The ref-code segment count (5 = Office, 6 = Program, 7 = Project, 8 = Activity) is only a
/// FALLBACK, used when B–E are all blank. It is not authoritative: in the province's real
/// FY2027 AIP, 82 of 2,887 rows have a segment count that contradicts the description column
/// — 5-segment codes on program rows (which produced nameless phantom offices), 9-segment
/// codes on activity rows (which were silently discarded), and so on.
///
/// Ref-code shape: a cell counts as a ref code only if it contains a '-' and no whitespace.
/// This keeps header text ("AIP Reference Code (1)") and footer rows ("TOTAL", "GRAND TOTAL")
/// out of the hierarchy even though they sit in column A.
///
/// Multi-line rows: if col A holds no ref code, the text is in col E, the row carries NO
/// amounts, and the previous row was an Activity, the col-E text is appended to that
/// activity's name. A blank-ref row that does carry amounts is a real activity whose author
/// left the ref code blank (or typed "None") — it becomes its own activity, because merging
/// it into the previous name would discard its money. A blank-ref row whose text is in col C
/// or D is likewise a real Program/Project node. All such recovered nodes get a synthetic ref
/// code of the form "{parentRefCode}-S{ordinal:000}".
///
/// Nothing carrying data is ever discarded silently: a node arriving without its parent gets
/// a synthesized "(unassigned)" parent rather than being dropped.
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
        int synthCounter = 0;

        for (int row = 1; row <= lastRow; row++)
        {
            string rawRefCode = ws.Cell(row, 1).GetString().Trim();
            bool hasRefCode = IsRefCodeLike(rawRefCode);

            AipLevel level = DetectLevel(ws, row, hasRefCode ? CountSegments(rawRefCode) : 0);

            if (!hasRefCode)
            {
                // No usable ref code. Either a continuation of the previous activity's name,
                // or a Program/Project node whose author left column A blank.
                if (level == AipLevel.Activity)
                {
                    // A continuation row is text only. If the row carries amounts of its own it
                    // is a real activity whose author left column A blank (or typed "None") —
                    // folding that into the previous activity's NAME silently discards its
                    // money, so it falls through and becomes its own activity instead.
                    if (!CarriesAmounts(ws, row))
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
                }

                // A blank-ref Office row is header furniture (the merged B8:E9 caption), never
                // data — only Program/Project/Activity nodes are recovered here.
                if (level is not (AipLevel.Program or AipLevel.Project or AipLevel.Activity)) continue;
            }
            else if (level == AipLevel.None)
            {
                continue;   // ref-code-shaped but no level could be resolved
            }

            // Strip the -XXX- placeholder before storing; segment count used the raw value.
            string storedRefCode = hasRefCode
                ? rawRefCode.Replace("-XXX-", "-", StringComparison.OrdinalIgnoreCase)
                : SynthRefCode(currentProject?.RefCode ?? currentProgram?.RefCode ?? currentOffice?.RefCode ?? sector, ++synthCounter);

            switch (level)
            {
                case AipLevel.Office:
                {
                    string name = ws.Cell(row, 2).GetString().Trim();
                    currentOffice = new ParsedAipOffice(storedRefCode, name, sector, new List<ParsedAipProgram>());
                    offices.Add(currentOffice);
                    currentProgram = null;
                    currentProject = null;
                    lastActivity   = null;
                    currentActivityList = null;
                    break;
                }
                case AipLevel.Program:
                {
                    currentOffice ??= AddUnassignedOffice(offices, storedRefCode, sector);
                    string name = ws.Cell(row, 3).GetString().Trim();
                    ParsedAipActivity? lineItem = TryParseLineItem(ws, row, storedRefCode, name);
                    currentProgram = new ParsedAipProgram(storedRefCode, name, new List<ParsedAipProject>(), lineItem);
                    ((List<ParsedAipProgram>)currentOffice.Programs).Add(currentProgram);
                    currentProject = null;
                    lastActivity   = null;
                    currentActivityList = null;
                    break;
                }
                case AipLevel.Project:
                {
                    currentOffice  ??= AddUnassignedOffice(offices, storedRefCode, sector);
                    currentProgram ??= AddUnassignedProgram(currentOffice, storedRefCode);
                    string name = ws.Cell(row, 4).GetString().Trim();
                    ParsedAipActivity? lineItem = TryParseLineItem(ws, row, storedRefCode, name);
                    currentProject = new ParsedAipProject(storedRefCode, name, new List<ParsedAipActivity>(), lineItem);
                    ((List<ParsedAipProject>)currentProgram.Projects).Add(currentProject);
                    lastActivity   = null;
                    currentActivityList = (List<ParsedAipActivity>)currentProject.Activities;
                    break;
                }
                case AipLevel.Activity:
                {
                    // An orphaned activity keeps its data: synthesize whatever parents are
                    // missing rather than discarding the row.
                    currentOffice  ??= AddUnassignedOffice(offices, storedRefCode, sector);
                    currentProgram ??= AddUnassignedProgram(currentOffice, storedRefCode);
                    currentProject ??= AddUnassignedProject(currentProgram, storedRefCode);

                    currentActivityList = (List<ParsedAipActivity>)currentProject.Activities;
                    LineItemFields f = ReadLineItemFields(ws, row);
                    ParsedAipActivity activity = new(
                        RefCode:           storedRefCode,
                        Name:              ws.Cell(row, 5).GetString().Trim(),
                        EsreCode:          f.EsreCode,
                        ImplementingOffice:f.ImplementingOffice,
                        StartDate:         f.StartDate,
                        EndDate:           f.EndDate,
                        ExpectedOutputs:   f.ExpectedOutputs,
                        FundingSourceRaw:  f.FundingSourceRaw,
                        Ps:                f.Ps,
                        Mooe:              f.Mooe,
                        Co:                f.Co,
                        Total:             f.Total,
                        CcAdaptation:      f.CcAdaptation,
                        CcMitigation:      f.CcMitigation,
                        CcTypologyCode:    f.CcTypologyCode);
                    currentActivityList.Add(activity);
                    lastActivity = activity;
                    break;
                }
            }
        }

        return offices;
    }

    // ── Level detection (RAL-238) ─────────────────────────────────────────────

    private enum AipLevel { None = 0, Office = 2, Program = 3, Project = 4, Activity = 5 }

    /// <summary>
    /// Resolves a row's hierarchy level. The description column wins: the first non-blank of
    /// B (Office), C (Program), D (Project), E (Activity). Only when all four are blank does
    /// the ref-code segment count decide.
    /// </summary>
    private static AipLevel DetectLevel(IXLWorksheet ws, int row, int segments)
    {
        for (int col = 2; col <= 5; col++)
            if (!string.IsNullOrWhiteSpace(ws.Cell(row, col).GetString()))
                return (AipLevel)col;

        return segments switch
        {
            5 => AipLevel.Office,
            6 => AipLevel.Program,
            7 => AipLevel.Project,
            8 => AipLevel.Activity,
            _ => AipLevel.None,
        };
    }

    /// <summary>
    /// A column-A value counts as a ref code only when it contains a '-' and no whitespace.
    /// Keeps sheet headers ("AIP Reference Code (1)") and footers ("TOTAL", "GRAND TOTAL")
    /// out of the hierarchy.
    /// </summary>
    private static bool IsRefCodeLike(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (value.Equals("None", StringComparison.OrdinalIgnoreCase)) return false;
        if (!value.Contains('-')) return false;
        foreach (char c in value)
            if (char.IsWhiteSpace(c)) return false;
        return true;
    }

    /// <summary>
    /// True when the row has a PS, MOOE or CO figure of its own. Deliberately narrower than
    /// <see cref="TryParseLineItem"/>'s check: a text-only continuation row that happens to
    /// carry a stray eSRE code still merges into the previous activity's name, but one
    /// carrying money never does.
    /// </summary>
    private static bool CarriesAmounts(IXLWorksheet ws, int row)
        => ParseDecimal(ws.Cell(row, 12)) is not null
        || ParseDecimal(ws.Cell(row, 13)) is not null
        || ParseDecimal(ws.Cell(row, 14)) is not null;

    private const string UnassignedName = "(unassigned)";

    private static string SynthRefCode(string basis, int ordinal) => $"{basis}-S{ordinal:000}";

    private static ParsedAipOffice AddUnassignedOffice(List<ParsedAipOffice> offices, string childRefCode, string sector)
    {
        ParsedAipOffice office = new(SynthRefCode(childRefCode, 0), UnassignedName, sector, new List<ParsedAipProgram>());
        offices.Add(office);
        return office;
    }

    private static ParsedAipProgram AddUnassignedProgram(ParsedAipOffice office, string childRefCode)
    {
        ParsedAipProgram program = new(SynthRefCode(childRefCode, 0), UnassignedName, new List<ParsedAipProject>());
        ((List<ParsedAipProgram>)office.Programs).Add(program);
        return program;
    }

    private static ParsedAipProject AddUnassignedProject(ParsedAipProgram program, string childRefCode)
    {
        ParsedAipProject project = new(SynthRefCode(childRefCode, 0), UnassignedName, new List<ParsedAipActivity>());
        ((List<ParsedAipProject>)program.Projects).Add(project);
        return project;
    }

    /// <summary>
    /// RAL-108: some program/project rows (6/7 segments) carry the same line-item columns
    /// (F–R) as an activity row — e.g. a program with amounts recorded directly on it and no
    /// child project. When any of those columns is non-blank, returns a
    /// <see cref="ParsedAipActivity"/> using the program/project's own ref code and name so it
    /// can be materialized as a synthetic leaf activity at confirm-import time. Returns null
    /// when the row carries no line-item data of its own (the normal case).
    /// </summary>
    private static ParsedAipActivity? TryParseLineItem(IXLWorksheet ws, int row, string refCode, string name)
    {
        LineItemFields f = ReadLineItemFields(ws, row);
        bool hasData = f.EsreCode is not null || f.ImplementingOffice is not null
            || f.StartDate is not null || f.EndDate is not null || f.ExpectedOutputs is not null
            || f.FundingSourceRaw is not null || f.Ps is not null || f.Mooe is not null
            || f.Co is not null || f.CcAdaptation is not null || f.CcMitigation is not null
            || f.CcTypologyCode is not null;

        return hasData
            ? new ParsedAipActivity(refCode, name, f.EsreCode, f.ImplementingOffice, f.StartDate,
                f.EndDate, f.ExpectedOutputs, f.FundingSourceRaw, f.Ps, f.Mooe, f.Co, f.Total,
                f.CcAdaptation, f.CcMitigation, f.CcTypologyCode)
            : null;
    }

    private readonly record struct LineItemFields(
        string? EsreCode, string? ImplementingOffice, string? StartDate, string? EndDate,
        string? ExpectedOutputs, string? FundingSourceRaw,
        decimal? Ps, decimal? Mooe, decimal? Co, decimal? Total,
        decimal? CcAdaptation, decimal? CcMitigation, string? CcTypologyCode);

    private static LineItemFields ReadLineItemFields(IXLWorksheet ws, int row)
    {
        decimal? ps   = ParseMoney(ws.Cell(row, 12));
        decimal? mooe = ParseMoney(ws.Cell(row, 13));
        decimal? co   = ParseMoney(ws.Cell(row, 14));
        return new LineItemFields(
            EsreCode:           NullIfBlank(ws.Cell(row, 6).GetString()),
            ImplementingOffice: NullIfBlank(ws.Cell(row, 7).GetString()),
            StartDate:          NullIfBlank(ws.Cell(row, 8).GetString()),
            EndDate:            NullIfBlank(ws.Cell(row, 9).GetString()),
            ExpectedOutputs:    NullIfBlank(ws.Cell(row, 10).GetString()),
            FundingSourceRaw:   NullIfBlank(ws.Cell(row, 11).GetString()),
            Ps:                 ps,
            Mooe:               mooe,
            Co:                 co,
            // Computed, NOT read from the source Total column (col 15) — same RAL-144 rule as a
            // real activity row: a blank/stale source Total must never desync from Ps+Mooe+Co.
            // Null only when Ps/Mooe/Co are ALL blank.
            Total:              (ps.HasValue || mooe.HasValue || co.HasValue)
                                    ? (ps ?? 0m) + (mooe ?? 0m) + (co ?? 0m)
                                    : null,
            CcAdaptation:       ParseMoney(ws.Cell(row, 16)),
            CcMitigation:       ParseMoney(ws.Cell(row, 17)),
            CcTypologyCode:     NullIfBlank(ws.Cell(row, 18).GetString()));
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

    /// <summary>
    /// The province's AIP workbook is denominated in ₱000 — a cell reading <c>250</c> means
    /// ₱250,000. Storage is PESOS since V18-35 (PPDO-34), so the file's own unit is converted
    /// here, at the import edge, and never again downstream.
    ///
    /// ⚠️ This is not a survivor of the ×1000 the migration deleted. Those six sites converted
    /// between two of OUR units and are gone. This one converts the SOURCE DOCUMENT's unit into
    /// ours, the same edge conversion the AIP detail page does for display — without it every
    /// upload would write thousands into a peso column and silently divide the record by 1000.
    /// </summary>
    private const decimal PesosPerWorkbookUnit = 1000m;

    /// <summary>A money cell from the workbook, converted from ₱000 to pesos. NULL stays NULL.</summary>
    private static decimal? ParseMoney(IXLCell cell) => ParseDecimal(cell) * PesosPerWorkbookUnit;

    private static decimal? ParseDecimal(IXLCell cell)
    {
        if (cell.DataType == XLDataType.Number)
            return (decimal)cell.GetDouble();
        string raw = cell.GetString().Trim();
        return decimal.TryParse(raw, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out decimal v) ? v : null;
    }
}
