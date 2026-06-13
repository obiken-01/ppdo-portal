namespace PPDO.Application.Common;

/// <summary>
/// Outcome of a config CSV upsert (RAL-70).
///   New      — rows whose key did not exist and were inserted.
///   Updated  — rows whose key existed and had at least one changed field.
///   Skipped  — rows whose key existed but were unchanged, plus invalid rows.
///   Errors   — human-readable messages for invalid rows (also counted in Skipped).
/// </summary>
public sealed record CsvImportResult(
    int New,
    int Updated,
    int Skipped,
    IReadOnlyList<string> Errors);
