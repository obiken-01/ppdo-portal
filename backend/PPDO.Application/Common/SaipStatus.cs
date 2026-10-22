namespace PPDO.Application.Common;

/// <summary>
/// An SAIP's state (v1.10.0 — PPDO-246, <c>docs/v1.10/SAIP_Spec.md</c> §5). The first four mirror the
/// AIP's own office workflow; <see cref="Approved"/> and <see cref="Cancelled"/> close an SAIP.
/// </summary>
public static class SaipStatus
{
    public const string Draft            = "Draft";
    public const string DepartmentReview = "DepartmentReview";
    public const string SubmittedToPpdo  = "SubmittedToPpdo";
    public const string ReturnedByPpdo   = "ReturnedByPpdo";
    public const string Approved         = "Approved";
    public const string Cancelled        = "Cancelled";

    public static readonly IReadOnlyList<string> All =
    [
        Draft, DepartmentReview, SubmittedToPpdo, ReturnedByPpdo, Approved, Cancelled,
    ];

    /// <summary>
    /// The two states that close an SAIP. ⚠️ The filtered unique index "one open SAIP per office"
    /// is written from this list (<c>AipSaipConfiguration</c>), so the rule and the index agree.
    /// </summary>
    public static readonly IReadOnlyList<string> Closed = [Approved, Cancelled];

    /// <summary>True while the SAIP still counts as the office's one open SAIP (decision 2).</summary>
    public static bool IsOpen(string status) => !Closed.Contains(status);
}
