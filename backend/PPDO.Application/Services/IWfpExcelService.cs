using PPDO.Application.Common;

namespace PPDO.Application.Services;

/// <summary>
/// Generates WFP Excel reports (.xlsx) from pre-loaded domain data.
/// Implemented in PPDO.Infrastructure/Services/ExcelService.cs using ClosedXML.
/// Defined here (Application layer) so WfpService can depend on it without
/// Infrastructure → Application circular references.
/// </summary>
public interface IWfpExcelService
{
    /// <summary>
    /// Builds the A4-landscape Work and Financial Plan Excel file.
    /// Layout: title block → two-level column headers → sector/program/project/activity
    /// hierarchy rows → expenditure-line rows → program subtotals → grand total.
    /// Returns the file as a byte array (.xlsx).
    /// </summary>
    byte[] GenerateWfpReport(WfpExcelReportData data);
}
