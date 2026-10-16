using System.Text;
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

    /// <summary>
    /// One office's file inside the ZIP, <c>WFP_OPA_2028.xlsx</c>: the per-office export's own name
    /// (decision 13), so a file means the same thing whichever way it was downloaded.
    /// </summary>
    public static string OfficeFileName(string officeCode, int fiscalYear)
        => $"WFP_{officeCode}_{fiscalYear}.xlsx";

    /// <summary>The note inside every ZIP that says what is in it and what was left out (decision 7).</summary>
    public const string ReadmeFileName = "README.txt";

    /// <summary>Where a job's ZIP is kept in the file store: <c>initial-wfp-exports/42/Initial WFP FY2028 - … .zip</c>.</summary>
    public static string FileKey(int jobId, int fiscalYear, DateTime builtAtUtc)
        => $"initial-wfp-exports/{jobId}/{ZipFileName(fiscalYear, builtAtUtc)}";

    /// <summary>
    /// <c>README.txt</c>: when the ZIP was built and for whom, the offices in it, and each office left
    /// out with the reason (decisions 7 and 9). Windows line endings, because it is opened in Notepad.
    /// </summary>
    public static string Readme(
        int fiscalYear,
        DateTime builtAtUtc,
        string requestedByName,
        IReadOnlyList<WfpReportOfficeDto> included,
        IReadOnlyList<SkippedOffice> skipped)
    {
        int total = included.Count + skipped.Count;
        StringBuilder text = new();
        void Line(string line = "") => text.Append(line).Append("\r\n");

        Line($"Initial WFP - FY {fiscalYear}");
        Line($"Built {builtAtUtc.Add(ManilaOffset):yyyy-MM-dd HH:mm} (Manila time) for {requestedByName}.");
        Line($"Offices whose FY {fiscalYear} AIP was marked as done at that time: {total}.");

        Line();
        Line($"Included ({included.Count}):");
        foreach (WfpReportOfficeDto office in included)
            Line($"  {OfficeFileName(office.OfficeCode, fiscalYear)}  {office.OfficeCode} - {office.OfficeName}");
        if (included.Count == 0) Line("  none");

        Line();
        Line($"Skipped ({skipped.Count}):");
        foreach (SkippedOffice skip in skipped)
            Line($"  {skip.Office.OfficeCode} - {skip.Office.OfficeName}: {skip.Reason}");
        if (skipped.Count == 0) Line("  none");

        return text.ToString();
    }

    /// <summary><c>problems_json</c> as written: <c>[{ "officeCode": "…", "reason": "…" }]</c>, or null when none.</summary>
    public static string? SerializeProblems(IReadOnlyList<InitialWfpExportProblemDto> problems)
        => problems.Count == 0 ? null : JsonSerializer.Serialize(problems, ProblemsJson);

    /// <summary>Reads <c>problems_json</c> back; null or empty is no problems.</summary>
    public static IReadOnlyList<InitialWfpExportProblemDto> ParseProblems(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<InitialWfpExportProblemDto>>(json, ProblemsJson) ?? [];
}

/// <summary>An office the worker could not build, and why, for <see cref="InitialWfpExportFiles.Readme"/>.</summary>
public sealed record SkippedOffice(WfpReportOfficeDto Office, string Reason);
