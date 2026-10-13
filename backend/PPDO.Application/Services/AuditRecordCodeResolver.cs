using System.Text.Json;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// PPDO-110 — what the Audit Log page shows in its Record column for an AIP program, project or
/// activity: the row's ref code (with its name on hover) instead of <c>#id</c>, which means nothing
/// to a reader. Every other table keeps <c>#id</c>.
///
/// <list type="number">
///   <item><b>The live row first.</b> One query per ref-coded table the page touches
///   (<see cref="IActivityLabelRepository.GetAipRecordLabelsAsync"/>), never one per row. Read-time
///   resolution is what makes this work for history already written. A renumbered row shows the
///   code it has now.</item>
///   <item><b>Then the audit row's own snapshot.</b> A deleted row has no live code, but its DELETE
///   snapshot carries <c>refCode</c> and <c>name</c>, as do CREATE snapshots and, since PPDO-110,
///   UPDATE ones.</item>
///   <item><b>Then nothing.</b> A null code, and the page falls back to <c>#id</c> — today's
///   behaviour, so never blank and never an error.</item>
/// </list>
/// </summary>
public sealed class AuditRecordCodeResolver
{
    private static readonly IReadOnlyDictionary<string, AipRecordKind> Kinds = new Dictionary<string, AipRecordKind>
    {
        ["aip_programs"]   = AipRecordKind.Program,
        ["aip_projects"]   = AipRecordKind.Project,
        ["aip_activities"] = AipRecordKind.Activity,
    };

    private readonly IActivityLabelRepository _labels;

    public AuditRecordCodeResolver(IActivityLabelRepository labels) => _labels = labels;

    /// <summary>One result per audit row, in the order given.</summary>
    public async Task<IReadOnlyList<AuditRecordCode>> ResolveAsync(
        IReadOnlyList<AuditLog> audits, CancellationToken cancellationToken = default)
    {
        if (audits.Count == 0) return [];

        // Pass 1: the ids per kind on this page. Sequential awaits — one DbContext (CLAUDE.md).
        Dictionary<AipRecordKind, IReadOnlyDictionary<int, AipRecordLabel>> live = [];
        foreach (IGrouping<AipRecordKind, int> group in audits
                     .Where(a => a.RecordId.HasValue && Kinds.ContainsKey(a.TableName))
                     .GroupBy(a => Kinds[a.TableName], a => a.RecordId!.Value))
        {
            live[group.Key] = await _labels.GetAipRecordLabelsAsync(group.Key, group.Distinct().ToList(), cancellationToken);
        }

        // Pass 2: per row, the live row, else the snapshot, else nothing.
        return audits.Select(a => Resolve(a, live)).ToList();
    }

    private static AuditRecordCode Resolve(
        AuditLog audit, IReadOnlyDictionary<AipRecordKind, IReadOnlyDictionary<int, AipRecordLabel>> live)
    {
        if (!Kinds.TryGetValue(audit.TableName, out AipRecordKind kind)) return AuditRecordCode.None;

        if (audit.RecordId is int id
            && live.TryGetValue(kind, out IReadOnlyDictionary<int, AipRecordLabel>? rows)
            && rows.TryGetValue(id, out AipRecordLabel? label))
            return new AuditRecordCode(label.RefCode, label.Name);

        JsonElement? newer = Parse(audit.NewValues);
        JsonElement? older = Parse(audit.OldValues);
        string? code = Str(newer, "refCode") ?? Str(older, "refCode");
        return code is null
            ? AuditRecordCode.None
            : new AuditRecordCode(code, Str(newer, "name") ?? Str(older, "name"));
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
            // A snapshot that will not parse costs this row its code, never the page.
            return null;
        }
    }

    private static string? Str(JsonElement? snapshot, string key)
        => snapshot is { } e && e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString() : null;
}

/// <summary>An audit row's ref code and name, both null when it has none (PPDO-110).</summary>
public sealed record AuditRecordCode(string? Code, string? Name)
{
    public static AuditRecordCode None { get; } = new(null, null);
}
