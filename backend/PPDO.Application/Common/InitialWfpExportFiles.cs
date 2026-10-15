using System.Text.Json;
using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Common;

/// <summary>
/// Names and stored formats for the bulk initial WFP export (v1.9.0 — PPDO-228), in one place so the
/// endpoints (PPDO-228) and the worker that writes them (PPDO-229) cannot drift apart.
/// </summary>
public static class InitialWfpExportFiles
{
    /// <summary>Manila is UTC+8 all year (no daylight saving).</summary>
    private static readonly TimeSpan ManilaOffset = TimeSpan.FromHours(8);

    private static readonly JsonSerializerOptions ProblemsJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The ZIP's download name, <c>Initial WFP FY2028 - 2026-10-15 1430.zip</c>, stamped in Manila time
    /// (spec decision 13).
    /// </summary>
    public static string ZipFileName(int fiscalYear, DateTime builtAtUtc)
        => $"Initial WFP FY{fiscalYear} - {builtAtUtc.Add(ManilaOffset):yyyy-MM-dd HHmm}.zip";

    /// <summary><c>problems_json</c> as written: <c>[{ "officeCode": "…", "reason": "…" }]</c>, or null when none.</summary>
    public static string? SerializeProblems(IReadOnlyList<InitialWfpExportProblemDto> problems)
        => problems.Count == 0 ? null : JsonSerializer.Serialize(problems, ProblemsJson);

    /// <summary>Reads <c>problems_json</c> back; null or empty is no problems.</summary>
    public static IReadOnlyList<InitialWfpExportProblemDto> ParseProblems(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<InitialWfpExportProblemDto>>(json, ProblemsJson) ?? [];
}
