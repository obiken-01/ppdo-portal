namespace PPDO.Application.Common;

/// <summary>
/// String constants for the AIP Program "Function Band" field (v1.4 WFP Rework — open
/// question Q1). Drives the (not-yet-built) WFP report generator's layout. Set at AIP program
/// level, captured during WFP data entry. Null/empty = not yet set.
/// </summary>
public static class AipFunctionBand
{
    public const string Core      = "CORE";
    public const string Strategic = "STRATEGIC";
    public const string Support   = "SUPPORT";
}
