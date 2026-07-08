namespace PPDO.Application.Common;

/// <summary>
/// String constants for the WFP expenditure "Nature" field (v1.4 WFP Rework — §5.3).
/// Default-only pre-fill from <c>Account.DefaultNature</c>; never an enforced gate — any
/// expenditure may use any of the three values regardless of its account's default.
/// </summary>
public static class WfpNature
{
    public const string Procurement    = "Procurement";
    public const string NonProcurement = "Non-Procurement";
    public const string Combined       = "Combined";
}
