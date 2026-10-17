using System.Text.RegularExpressions;

namespace PPDO.Application.Common;

/// <summary>
/// The <c>notifications.type</c> values (v1.9.0 — PPDO-221, <c>Notification_Centre_Spec.md</c> §3.1).
/// Stored as written, so they are part of the frontend contract: rename one and old rows keep the old name.
/// </summary>
public static class NotificationTypes
{
    public const string ProposalComment             = "ProposalComment";
    public const string ProposalReply               = "ProposalReply";
    public const string ProposalResolved            = "ProposalResolved";
    public const string AipReturnedByPpdo           = "AipReturnedByPpdo";
    public const string AipReturnedByDepartmentHead = "AipReturnedByDepartmentHead";
    public const string AipSentToPpdo               = "AipSentToPpdo";
    public const string AipDivisionSubmitted        = "AipDivisionSubmitted";
    public const string AipReviewerEdit             = "AipReviewerEdit";
    public const string InitialWfpExportReady       = "InitialWfpExportReady";
    public const string AipComment                  = "AipComment";
    public const string AipReply                    = "AipReply";
    public const string AipResolved                 = "AipResolved";
}

/// <summary>
/// The frozen sentences of §3.1 (decision 9). Pure and static so each one is tested on its own and no
/// hook formats its own variant.
/// </summary>
public static class NotificationText
{
    /// <summary>The column width (<c>notifications.text</c>, nvarchar(400)).</summary>
    public const int MaxLength = 400;

    /// <summary>How much of a comment the entry quotes.</summary>
    public const int QuoteLength = 80;

    public static string ProposalThreadStarted(string side, string refCode, string project, string body)
        => Fit($"{side} commented on the proposal for {Target(refCode, project)}: \"{Quote(body)}\"");

    public static string ProposalReply(string name, string refCode, string project, string body)
        => Fit($"{name} replied on the proposal for {Target(refCode, project)}: \"{Quote(body)}\"");

    public static string ProposalResolved(string name, string refCode, string project)
        => Fit($"{name} resolved your comment on the proposal for {Target(refCode, project)}");

    /// <summary>"PPDO returned your FY 2028 AIP with 3 comments"; no count when there are none open.</summary>
    public static string AipReturnedByPpdo(int fiscalYear, int openComments)
        => openComments switch
        {
            <= 0 => $"PPDO returned your FY {fiscalYear} AIP",
            1    => $"PPDO returned your FY {fiscalYear} AIP with 1 comment",
            _    => $"PPDO returned your FY {fiscalYear} AIP with {openComments} comments",
        };

    public static string AipReturnedByDepartmentHead(string name, int fiscalYear)
        => Fit($"{name} returned your FY {fiscalYear} AIP work");

    public static string AipSentToPpdo(string office, int fiscalYear)
        => Fit($"{office} sent its FY {fiscalYear} AIP to PPDO");

    public static string AipDivisionSubmitted(string division, int fiscalYear)
        => Fit($"{division} submitted its FY {fiscalYear} AIP work");

    /// <summary>"PPDO edited Seed distribution: 5-02-03-010 Office Supplies CO ₱40,000.00 → ₱50,000.00"</summary>
    public static string AipReviewerEdited(string activity, string change)
        => Fit($"PPDO edited {Name(activity)}: {change}");

    /// <summary>"PPDO added 5-02-01-010 Traveling Expenses - Local to Seed distribution"</summary>
    public static string AipReviewerAdded(string account, string activity)
        => Fit($"PPDO added {account} to {Name(activity)}");

    /// <summary>"PPDO commented on Seed distribution: "Check the unit cost"" (PPDO-223).</summary>
    public static string AipThreadStarted(string side, string activity, string body)
        => Fit($"{side} commented on {Name(activity)}: \"{Quote(body)}\"");

    /// <summary>"Ana Reyes replied on Seed distribution: "Fixed"" (PPDO-223).</summary>
    public static string AipReply(string name, string activity, string body)
        => Fit($"{name} replied on {Name(activity)}: \"{Quote(body)}\"");

    /// <summary>"Juan Cruz resolved your comment on Seed distribution" (PPDO-223).</summary>
    public static string AipResolved(string name, string activity)
        => Fit($"{name} resolved your comment on {Name(activity)}");

    /// <summary>
    /// "Your FY 2028 initial WFP export is ready (12 offices)", or "(11 of 12 offices)" when some were
    /// skipped (Bulk_Initial_WFP_Export_Spec.md §3 cases 4 and 5).
    /// </summary>
    public static string InitialWfpExportReady(int fiscalYear, int built, int total)
    {
        string offices = built == total
            ? $"{total} {(total == 1 ? "office" : "offices")}"
            : $"{built} of {total} {(total == 1 ? "office" : "offices")}";
        return Fit($"Your FY {fiscalYear} initial WFP export is ready ({offices})");
    }

    private static string Target(string refCode, string project)
        => string.IsNullOrWhiteSpace(refCode) ? Name(project) : $"{refCode} — {Name(project)}";

    /// <summary>
    /// A name on one line, cut like a quote. ⚠️ An AIP activity name is free text and can be a whole
    /// multi-line paragraph; left whole it filled the 400-character column and <see cref="Fit"/> cut
    /// the sentence before the change it exists to report ("PPDO edited Manpower augmentation…").
    /// </summary>
    public static string Name(string name) => Quote(name);

    /// <summary>The first <see cref="QuoteLength"/> characters on one line, with an ellipsis when cut.</summary>
    public static string Quote(string body)
    {
        string oneLine = Regex.Replace(body ?? string.Empty, @"\s+", " ").Trim();
        return oneLine.Length <= QuoteLength ? oneLine : oneLine[..QuoteLength].TrimEnd() + "…";
    }

    /// <summary>Cuts to the column width. A long project name must never make the insert fail.</summary>
    public static string Fit(string text)
        => text.Length <= MaxLength ? text : text[..(MaxLength - 1)] + "…";
}

/// <summary>Portal-relative links an entry opens (§3.1). The frontend owns the routes; these mirror them.</summary>
public static class NotificationLinks
{
    /// <summary>The proposal editor with its comments panel open (<c>comments=open</c> is read by PPDO-222).</summary>
    public static string Proposal(int proposalId)
        => $"/budget-planning/proposals/edit?id={proposalId}&comments=open";

    public static string AipEntry(int fiscalYear)
        => $"/budget-planning/aip/entry?fiscalYear={fiscalYear}";

    public static string AipReview(int officeId, int fiscalYear)
        => $"/budget-planning/aip/review?officeId={officeId}&fiscalYear={fiscalYear}";

    /// <summary>The report page on that year, where the initial WFP and its <b>Download all</b> live.</summary>
    public static string WfpReport(int fiscalYear)
        => $"/budget-planning/report?fiscalYear={fiscalYear}";

    public static string AipActivity(int fiscalYear, int programId, int projectId, int activityId)
        => $"/budget-planning/aip/entry?fiscalYear={fiscalYear}&programId={programId}&projectId={projectId}&activityId={activityId}";

    /// <summary>The review drill-down on one activity — where a PPDO reviewer reads a thread (PPDO-223).</summary>
    public static string AipReviewActivity(int officeId, int fiscalYear, int programId, int projectId, int activityId)
        => $"/budget-planning/aip/review?officeId={officeId}&fiscalYear={fiscalYear}&programId={programId}&projectId={projectId}&activityId={activityId}";
}
