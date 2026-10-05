using System.Globalization;
using System.Text;
using System.Text.Json;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Turns a page of audit rows into the sentences the dashboard's Recent activity band shows
/// (PPDO-181 / B4): "updated PTO's FY 2028 ceiling to ₱10,000,000.00", not
/// "update on budget_ceilings #13".
///
/// Each sentence is a verb phrase that follows the actor's name ("Jose Santos" + "moved an activity
/// in Rice Project 1 (OPA) from Cash to Admin."). Three rules:
///
/// <list type="number">
///   <item><b>Set-based.</b> The labels behind every sentence — office codes, project names,
///   division names — come from at most ONE query per table the page touches
///   (<see cref="IActivityLabelRepository"/>), gathered in a first pass over the rows. Never one
///   query per row, and sequential awaits only (one DbContext).</item>
///   <item><b>Always a sentence.</b> A record deleted after the audit row was written has no label;
///   the row falls back to a generic sentence ("updated a budget ceiling"). A snapshot that will
///   not parse does the same. No path ever shows a table name, an id or an action code.</item>
///   <item><b>The snapshot first, the table second.</b> The audit row's own old/new JSON already
///   carries amounts, division ids and office ids, so a lookup is only for what a snapshot lacks —
///   and a deleted activity can still be named from its snapshot.</item>
/// </list>
///
/// ⚠️ What is logged is not changed here — this only reads it. The mapping keys on the action
/// constants in <see cref="AuditAction"/>.
/// </summary>
public sealed class RecentActivityDescriber
{
    private const int NameLimit = 48;

    private readonly IActivityLabelRepository _labels;

    public RecentActivityDescriber(IActivityLabelRepository labels) => _labels = labels;

    /// <summary>One description per audit row, in the order given.</summary>
    public async Task<IReadOnlyList<string>> DescribeAsync(
        IReadOnlyList<AuditLog> audits, CancellationToken cancellationToken = default)
    {
        if (audits.Count == 0) return [];

        List<Row> rows = audits.Select(a => new Row(a, Parse(a.OldValues), Parse(a.NewValues))).ToList();
        Lookups lookups = await LoadAsync(rows, cancellationToken);

        return rows.Select(r => Describe(r, lookups)).ToList();
    }

    // ── Pass 1: which ids does this page touch? One query per table. ──────────

    private async Task<Lookups> LoadAsync(IReadOnlyList<Row> rows, CancellationToken ct)
    {
        HashSet<int> ceilings = [], aipOffices = [], programs = [], activities = [];
        HashSet<int> officeCodes = [], divisions = [], funds = [];

        foreach (Row r in rows)
        {
            switch (r.Audit.TableName)
            {
                case "budget_ceilings":
                    Add(ceilings, r.Audit.RecordId);
                    break;

                case "division_allocations":
                    Add(divisions, Int(r.New, "divisionId"));
                    Add(funds, Int(r.New, "fundingSourceId"));
                    break;

                case "aip_activities":
                    if (r.Audit.Action == AuditAction.RetagActivityDivision)
                    {
                        Add(activities, r.Audit.RecordId);
                        Add(divisions, Int(r.Old, "divisionId"));
                        Add(divisions, Int(r.New, "divisionId"));
                    }
                    else if (r.Audit.Action == AuditAction.Delete)
                        Add(aipOffices, Int(r.Old, "aipOfficeId"));
                    else
                        Add(activities, r.Audit.RecordId);
                    break;

                case "aip_programs":
                    if (r.Audit.Action == AuditAction.Create)
                        Add(aipOffices, Int(r.New, "officeId"));
                    else if (r.Audit.Action == AuditAction.Delete)
                        Add(aipOffices, Int(r.Old, "officeId"));
                    else
                        Add(programs, r.Audit.RecordId);
                    break;

                case "aip_offices":
                    if (r.Audit.Action != AuditAction.Delete)
                        Add(aipOffices, r.Audit.RecordId);
                    break;

                case "aip_division_submissions":
                    Add(officeCodes, Int(r.New, "officeId"));
                    Add(divisions, Int(r.New, "divisionId"));
                    break;

                case "program_divisions":
                    Add(divisions, Int(r.New, "divisionId") ?? Int(r.Old, "divisionId"));
                    break;
            }
        }

        // Sequential awaits — never Task.WhenAll over the shared DbContext (CLAUDE.md).
        return new Lookups(
            Ceilings:   ceilings.Count   > 0 ? await _labels.GetCeilingLabelsAsync(ceilings, ct)         : Empty<CeilingLabel>(),
            AipOffices: aipOffices.Count > 0 ? await _labels.GetAipOfficeLabelsAsync(aipOffices, ct)     : Empty<AipOfficeLabel>(),
            Programs:   programs.Count   > 0 ? await _labels.GetAipProgramLabelsAsync(programs, ct)      : Empty<AipProgramLabel>(),
            Activities: activities.Count > 0 ? await _labels.GetAipActivityLabelsAsync(activities, ct)   : Empty<AipActivityLabel>(),
            OfficeCodes: officeCodes.Count > 0 ? await _labels.GetOfficeCodesAsync(officeCodes, ct)      : Empty<string>(),
            Divisions:  divisions.Count  > 0 ? await _labels.GetDivisionNamesAsync(divisions, ct)        : Empty<string>(),
            Funds:      funds.Count      > 0 ? await _labels.GetFundingSourceNamesAsync(funds, ct)       : Empty<string>());
    }

    // ── Pass 2: one sentence per row ──────────────────────────────────────────

    private static string Describe(Row r, Lookups l) => r.Audit.TableName switch
    {
        "budget_ceilings"          => Ceiling(r, l),
        "division_allocations"     => Allocation(r, l),
        "aip_activities"           => Activity(r, l),
        "aip_programs"             => Program(r, l),
        "aip_offices"              => OfficeGroup(r, l),
        "aip_division_submissions" => DivisionSubmission(r, l),
        "program_divisions"        => ProgramDivision(r, l),
        _                          => Generic(r),
    };

    private static string Ceiling(Row r, Lookups l)
    {
        string verb = r.Audit.Action == AuditAction.Create ? "set" : "updated";
        if (r.Audit.RecordId is int id && l.Ceilings.TryGetValue(id, out CeilingLabel? c) && c.OfficeCode is not null)
        {
            string to = Decimal(r.New, "amount") is decimal amount ? $" to {Money(amount)}" : string.Empty;
            return $"{verb} {c.OfficeCode}'s FY {c.FiscalYear} ceiling{to}.";
        }
        return r.Audit.Action == AuditAction.Delete ? "removed a budget ceiling." : $"{verb} a budget ceiling.";
    }

    private static string Allocation(Row r, Lookups l)
    {
        if (Decimal(r.New, "amount") is decimal amount
            && Int(r.New, "divisionId") is int divisionId
            && l.Divisions.TryGetValue(divisionId, out string? division))
        {
            string fund = Int(r.New, "fundingSourceId") is int fundId && l.Funds.TryGetValue(fundId, out string? f)
                ? $" of the {Clip(f)}"
                : string.Empty;
            return $"allocated {Money(amount)}{fund} to {Clip(division)}.";
        }
        return r.Audit.Action switch
        {
            AuditAction.Create => "set a division allocation.",
            AuditAction.Delete => "removed a division allocation.",
            _                  => "updated a division allocation.",
        };
    }

    private static string Activity(Row r, Lookups l)
    {
        string? where = null;
        if (r.Audit.Action != AuditAction.Delete && r.Audit.RecordId is int id
            && l.Activities.TryGetValue(id, out AipActivityLabel? a))
            where = Where(a.ProjectName, a.OfficeCode);

        switch (r.Audit.Action)
        {
            case AuditAction.RetagActivityDivision:
            {
                string? from = Name(l.Divisions, Int(r.Old, "divisionId"));
                string? to   = Name(l.Divisions, Int(r.New, "divisionId"));
                string place = where is null ? string.Empty : $" in {where}";
                if (to is null)
                    return where is null
                        ? "moved an activity to another division."
                        : $"removed the division from an activity in {where}.";
                return from is null
                    ? $"moved an activity{place} to {to}."
                    : $"moved an activity{place} from {from} to {to}.";
            }
            case AuditAction.Create:
                return where is null ? "added an activity." : $"added an activity in {where}.";
            case AuditAction.Delete:
            {
                // The row is gone, but its snapshot still names it.
                string? name = Str(r.Old, "name");
                if (name is null) return "deleted an activity.";
                string office = Int(r.Old, "aipOfficeId") is int g
                    && l.AipOffices.TryGetValue(g, out AipOfficeLabel? o) && o.OfficeCode is not null
                    ? $" ({o.OfficeCode})"
                    : string.Empty;
                return $"deleted the activity \"{Clip(name)}\"{office}.";
            }
            default:
                return where is null ? "edited an activity." : $"edited an activity in {where}.";
        }
    }

    private static string Program(Row r, Lookups l)
    {
        switch (r.Audit.Action)
        {
            case AuditAction.Create:
            {
                string? name = Str(r.New, "name");
                if (name is null) return "added a program.";
                string office = OfficeOfGroup(r.New, "officeId", l) is { } code ? $" to {code}" : string.Empty;
                return $"added the program \"{Clip(name)}\"{office}.";
            }
            case AuditAction.Delete:
            {
                string? name = Str(r.Old, "name");
                return name is null ? "deleted a program." : $"deleted the program \"{Clip(name)}\".";
            }
            default:
                return r.Audit.RecordId is int id && l.Programs.TryGetValue(id, out AipProgramLabel? p)
                    ? $"edited the program \"{Clip(p.Name)}\"{(p.OfficeCode is null ? string.Empty : $" ({p.OfficeCode})")}."
                    : "edited a program.";
        }
    }

    private static string OfficeGroup(Row r, Lookups l)
    {
        AipOfficeLabel? g = r.Audit.RecordId is int id && l.AipOffices.TryGetValue(id, out AipOfficeLabel? found) ? found : null;
        // "OPA's FY 2028 AIP" — null when the group is gone or not linked to an office.
        string? aip = g?.OfficeCode is { } code ? $"{code}'s FY {g.FiscalYear} AIP" : null;

        return r.Audit.Action switch
        {
            AuditAction.SubmitToDeptHead => aip is null ? "submitted an office's AIP for department review." : $"submitted {aip} for department review.",
            AuditAction.ReturnToEncoder  => aip is null ? "returned an office's AIP to its encoders."        : $"returned {aip} to its encoders.",
            AuditAction.SubmitToPpdo     => aip is null ? "sent an office's AIP to PPDO."                    : $"sent {aip} to PPDO.",
            AuditAction.ReturnByPpdo     => aip is null ? "returned an office's AIP for changes."            : $"returned {aip} for changes.",
            AuditAction.AcceptByPpdo     => aip is null ? "accepted an office's AIP."                        : $"accepted {aip}.",
            AuditAction.ReopenByPpdo     => aip is null ? "re-opened an office's accepted AIP."              : $"re-opened {g!.OfficeCode}'s accepted FY {g.FiscalYear} AIP.",
            AuditAction.Create           => aip is null ? "added a sector group to an AIP."                  : $"added a sector group to {aip}.",
            AuditAction.Delete           => "removed a sector group from an AIP.",
            _                            => aip is null ? "updated a sector group in an AIP."                : $"updated a sector group in {aip}.",
        };
    }

    private static string DivisionSubmission(Row r, Lookups l)
    {
        string? division = Name(l.Divisions, Int(r.New, "divisionId"));
        string office = Int(r.New, "officeId") is int o && l.OfficeCodes.TryGetValue(o, out string? code)
            ? $" ({code})"
            : string.Empty;

        return r.Audit.Action switch
        {
            AuditAction.SubmitDivision => division is null
                ? $"submitted a division's work to the department head{office}."
                : $"submitted the {division}'s work to the department head{office}.",
            AuditAction.ReturnDivision => division is null
                ? $"returned a division's work to its encoders{office}."
                : $"returned the {division}'s work to its encoders{office}.",
            _ => $"updated a division's submission{office}.",
        };
    }

    private static string ProgramDivision(Row r, Lookups l)
    {
        string? division = Name(l.Divisions, Int(r.New, "divisionId") ?? Int(r.Old, "divisionId"));
        return r.Audit.Action switch
        {
            AuditAction.Create => division is null ? "assigned a program to a division." : $"assigned a program to {division}.",
            AuditAction.Delete => division is null ? "removed a program's division assignment." : $"removed a program from {division}.",
            _                  => "changed a program's division assignment.",
        };
    }

    /// <summary>Tables the band shows but that need no label (or whose label would cost a query for little).</summary>
    private static string Generic(Row r)
    {
        string action = r.Audit.Action;
        return r.Audit.TableName switch
        {
            "aip_records" => action == AuditAction.Create ? "created an AIP record."
                : action == AuditAction.Delete ? "deleted an AIP record."
                : Str(r.New, "status") == "Archived" ? "archived an AIP record."
                : "updated an AIP record.",
            "ldip_records" => action == AuditAction.Create ? "created an LDIP record."
                : action == AuditAction.Delete ? "deleted an LDIP record."
                : "updated an LDIP record.",
            "wfp_records" => action == AuditAction.Create ? "created a work and financial plan."
                : action == AuditAction.Delete ? "deleted a work and financial plan."
                : "updated a work and financial plan.",
            "wfp_expenditures" => action == AuditAction.Create ? "added a WFP expenditure."
                : action == AuditAction.Delete ? "removed a WFP expenditure."
                : "edited a WFP expenditure.",
            _ => "made a change.",
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string Where(string name, string? officeCode)
        => officeCode is null ? Clip(name) : $"{Clip(name)} ({officeCode})";

    /// <summary>The office code behind an aip_offices id carried in a snapshot, or null.</summary>
    private static string? OfficeOfGroup(JsonElement? snapshot, string key, Lookups l)
        => Int(snapshot, key) is int id && l.AipOffices.TryGetValue(id, out AipOfficeLabel? g) ? g.OfficeCode : null;

    private static string? Name(IReadOnlyDictionary<int, string> map, int? id)
        => id is int i && map.TryGetValue(i, out string? name) ? Clip(name) : null;

    /// <summary>One line, bounded. A multi-line activity name must not blow up a one-line feed.</summary>
    private static string Clip(string text)
    {
        StringBuilder sb = new(text.Length);
        bool space = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c)) { space = sb.Length > 0; continue; }
            if (space) { sb.Append(' '); space = false; }
            sb.Append(c);
        }
        string flat = sb.ToString();
        return flat.Length <= NameLimit ? flat : flat[..(NameLimit - 1)].TrimEnd() + "…";
    }

    private static string Money(decimal amount)
        => "₱" + amount.ToString("N2", CultureInfo.InvariantCulture);

    private static void Add(HashSet<int> set, int? id)
    {
        if (id is int i) set.Add(i);
    }

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? doc.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            // A snapshot that will not parse must not take the band down — the row degrades to
            // its generic sentence.
            return null;
        }
    }

    private static int? Int(JsonElement? snapshot, string key)
        => snapshot is { } e && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out int n)
            ? n : null;

    private static decimal? Decimal(JsonElement? snapshot, string key)
        => snapshot is { } e && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetDecimal(out decimal d)
            ? d : null;

    private static string? Str(JsonElement? snapshot, string key)
        => snapshot is { } e && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString() : null;

    private static IReadOnlyDictionary<int, T> Empty<T>() => new Dictionary<int, T>();

    private sealed record Row(AuditLog Audit, JsonElement? Old, JsonElement? New);

    private sealed record Lookups(
        IReadOnlyDictionary<int, CeilingLabel>     Ceilings,
        IReadOnlyDictionary<int, AipOfficeLabel>   AipOffices,
        IReadOnlyDictionary<int, AipProgramLabel>  Programs,
        IReadOnlyDictionary<int, AipActivityLabel> Activities,
        IReadOnlyDictionary<int, string>           OfficeCodes,
        IReadOnlyDictionary<int, string>           Divisions,
        IReadOnlyDictionary<int, string>           Funds);
}
