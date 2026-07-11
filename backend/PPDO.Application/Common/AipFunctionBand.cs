namespace PPDO.Application.Common;

/// <summary>
/// String constants for the AIP Program "Function Band" field (v1.4 WFP Rework — open
/// question Q1). Drives the WFP report's section grouping (WfpReportService). Set at AIP
/// program level, captured during WFP data entry. Required — AipService.UpdateProgramFunctionBandAsync
/// rejects null/empty, and new programs default to Core at AIP import time, so the column is
/// only ever null for legacy rows created before this field existed.
/// </summary>
public static class AipFunctionBand
{
    public const string Core      = "CORE";
    public const string Strategic = "STRATEGIC";
    public const string Support   = "SUPPORT";
}
