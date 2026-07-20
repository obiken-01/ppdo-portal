using System.Text;
using System.Text.Json;

namespace PPDO.Application.Common;

/// <summary>
/// Builds a human-readable, multi-line description of an audit_log row from its Action and
/// OldValues/NewValues JSON snapshots. Table-agnostic: it works purely by diffing whatever
/// fields the caller logged via <c>IAuditService.LogAsync</c>, so no changes are needed here
/// when a new service starts writing audit entries.
/// </summary>
public static class AuditDescriptionBuilder
{
    public static string Build(string action, string? oldValuesJson, string? newValuesJson)
    {
        Dictionary<string, JsonElement>? oldValues = Parse(oldValuesJson);
        Dictionary<string, JsonElement>? newValues = Parse(newValuesJson);

        // Every DELETE call site in this codebase is a soft delete: oldValues = { IsActive: true },
        // newValues = null. If that shape ever changes, this still reads fine as a stand-alone line.
        if (action == AuditAction.Delete)
            return "Deactivated";

        if (oldValues is null && newValues is not null)
            return DescribeFields(action == AuditAction.Create ? "Created with" : "Recorded", newValues);

        if (oldValues is not null && newValues is not null)
        {
            List<string> lines = new();
            foreach ((string key, JsonElement newValue) in newValues)
            {
                string newText = Format(newValue);
                string oldText = oldValues.TryGetValue(key, out JsonElement oldValue) ? Format(oldValue) : "—";
                if (oldText != newText)
                    lines.Add($"- {FriendlyName(key)}: {oldText} → {newText}");
            }
            return lines.Count > 0
                ? "Updated:\n" + string.Join('\n', lines)
                : "No visible field changes.";
        }

        return "No details recorded.";
    }

    private static string DescribeFields(string verb, Dictionary<string, JsonElement> values)
    {
        IEnumerable<string> lines = values.Select(kv => $"- {FriendlyName(kv.Key)}: {Format(kv.Value)}");
        return $"{verb}:\n" + string.Join('\n', lines);
    }

    private static Dictionary<string, JsonElement>? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Format(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => "—",
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.String => value.GetString() is { Length: > 0 } s ? s : "—",
        _ => value.ToString(),
    };

    /// <summary>camelCase field name (as serialized by AuditService) to spaced Title Case,
    /// e.g. "isActive" -> "Is Active", "overrideCanManageConfig" -> "Override Can Manage Config".</summary>
    private static string FriendlyName(string fieldName)
    {
        StringBuilder sb = new();
        for (int i = 0; i < fieldName.Length; i++)
        {
            char c = fieldName[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(fieldName[i - 1]))
                sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }
}
