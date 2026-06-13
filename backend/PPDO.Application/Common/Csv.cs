using System.Text;

namespace PPDO.Application.Common;

/// <summary>
/// Minimal RFC-4180-style CSV reader/writer for config seed upload/download (RAL-70).
/// Handles quoted fields, embedded commas, embedded quotes (<c>""</c> → <c>"</c>),
/// CR/LF/CRLF line endings, and a leading UTF-8 BOM. No external dependency.
/// </summary>
public static class Csv
{
    /// <summary>
    /// Parses CSV text into rows of fields, including the header row.
    /// Blank lines are skipped. The caller decides how to treat the header row.
    /// </summary>
    public static List<string[]> Parse(string text)
    {
        List<string[]> rows = new();
        if (string.IsNullOrEmpty(text))
            return rows;

        // Strip a leading UTF-8 BOM if present.
        if (text[0] == '﻿')
            text = text[1..];

        StringBuilder field = new();
        List<string> current = new();
        bool inQuotes = false;
        bool rowHasContent = false;

        void EndField()
        {
            current.Add(field.ToString());
            field.Clear();
        }

        void EndRow()
        {
            EndField();
            // Skip fully blank lines (single empty field, no content seen).
            if (rowHasContent || current.Count > 1)
                rows.Add(current.ToArray());
            current = new List<string>();
            rowHasContent = false;
        }

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i += 2; continue; }
                    inQuotes = false; i++; continue;
                }
                field.Append(c); rowHasContent = true; i++; continue;
            }

            switch (c)
            {
                case '"':  inQuotes = true; rowHasContent = true; i++; break;
                case ',':  EndField(); i++; break;
                case '\r': i++; break;            // CR is ignored; LF ends the row
                case '\n': EndRow(); i++; break;
                default:   field.Append(c); rowHasContent = true; i++; break;
            }
        }

        // Flush the final row when the text does not end in a newline.
        if (rowHasContent || field.Length > 0 || current.Count > 0)
            EndRow();

        return rows;
    }

    /// <summary>Writes a header row plus data rows as CSV text (CRLF line endings).</summary>
    public static string Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        StringBuilder sb = new();
        sb.Append(string.Join(",", headers.Select(Escape))).Append("\r\n");
        foreach (IReadOnlyList<string?> row in rows)
            sb.Append(string.Join(",", row.Select(Escape))).Append("\r\n");
        return sb.ToString();
    }

    private static string Escape(string? field)
    {
        field ??= string.Empty;
        bool mustQuote = field.Contains('"') || field.Contains(',')
                      || field.Contains('\n') || field.Contains('\r');
        return mustQuote ? $"\"{field.Replace("\"", "\"\"")}\"" : field;
    }

    /// <summary>
    /// Parses a CSV is_active cell. Accepts true/false/1/0/yes/no (case-insensitive).
    /// Blank defaults to <paramref name="fallback"/> (true).
    /// </summary>
    public static bool ParseBool(string? cell, bool fallback = true)
    {
        if (string.IsNullOrWhiteSpace(cell)) return fallback;
        return cell.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "y" => true,
            "false" or "0" or "no" or "n" => false,
            _ => fallback,
        };
    }
}
