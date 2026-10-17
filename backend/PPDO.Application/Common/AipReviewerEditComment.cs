using System.Globalization;

namespace PPDO.Application.Common;

/// <summary>
/// The text of the comment a PPDO reviewer's edit leaves on the activity (v1.9.0 — PPDO-212,
/// <c>docs/v1.9/AIP_Reviewer_Edit_Spec.md</c> decision 6). Pure and static, like
/// <see cref="HgdgAttribution"/>.
///
/// <para>
/// Written by the system, never typed: the office reads it to learn <b>what</b> changed, so it states
/// the account, the old and new figure per column, and the reviewer's reason. The structured
/// before/after also goes to the audit log; this is the human-readable copy the office actually sees.
/// </para>
/// </summary>
public static class AipReviewerEditComment
{
    /// <summary>Longest reason a reviewer may give, in characters, after trimming.</summary>
    public const int MaxReasonLength = 500;

    public const string ReasonRequiredMessage =
        "A reason is required for a PPDO edit (up to 500 characters).";

    /// <summary>The trimmed reason, or null when it is missing, blank, or too long.</summary>
    public static string? NormaliseReason(string? reason)
    {
        string trimmed = reason?.Trim() ?? string.Empty;
        return trimmed.Length is 0 or > MaxReasonLength ? null : trimmed;
    }

    /// <summary>"PPDO edited 5-02-03-010 Office Supplies: MOOE ₱60,000.00 → ₱120,000.00. Reason: …"</summary>
    public static string Edited(
        string? accountNumber, string? accountTitle,
        (decimal Ps, decimal Mooe, decimal Co) before,
        (decimal Ps, decimal Mooe, decimal Co) after,
        string reason)
        => $"PPDO edited {Account(accountNumber, accountTitle)}: {Changes(before, after)}. Reason: {reason}";

    /// <summary>
    /// "MOOE ₱60,000.00 → ₱120,000.00", one entry per changed column. Shared with the notification
    /// sentence (PPDO-221), so the bell and the comment never describe one edit differently.
    /// </summary>
    public static string Changes((decimal Ps, decimal Mooe, decimal Co) before, (decimal Ps, decimal Mooe, decimal Co) after)
    {
        List<string> changes = [];
        void Compare(string column, decimal old, decimal now)
        {
            if (old != now) changes.Add($"{column} {Peso(old)} → {Peso(now)}");
        }
        Compare("PS",   before.Ps,   after.Ps);
        Compare("MOOE", before.Mooe, after.Mooe);
        Compare("CO",   before.Co,   after.Co);

        return changes.Count == 0 ? "amounts unchanged" : string.Join("; ", changes);
    }

    /// <summary>"PPDO added 5-02-01-010 Traveling Expenses - Local: MOOE ₱45,500.00. Reason: …"</summary>
    public static string Added(
        string? accountNumber, string? accountTitle, decimal ps, decimal mooe, decimal co, string reason)
    {
        List<string> amounts = [];
        if (ps   != 0m) amounts.Add($"PS {Peso(ps)}");
        if (mooe != 0m) amounts.Add($"MOOE {Peso(mooe)}");
        if (co   != 0m) amounts.Add($"CO {Peso(co)}");

        string what = amounts.Count == 0 ? Peso(0m) : string.Join("; ", amounts);
        return $"PPDO added {Account(accountNumber, accountTitle)}: {what}. Reason: {reason}";
    }

    /// <summary>"5-02-03-010 Office Supplies", or "a line with no account".</summary>
    public static string Account(string? number, string? title)
    {
        string label = $"{number} {title}".Trim();
        return label.Length == 0 ? "a line with no account" : label;
    }

    // Invariant culture, so the separators never depend on the server's locale.
    private static string Peso(decimal value) => "₱" + value.ToString("N2", CultureInfo.InvariantCulture);
}
