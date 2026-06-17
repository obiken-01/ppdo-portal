using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Common;

/// <summary>
/// Data container passed to <see cref="Services.IWfpExcelService.GenerateWfpReport"/>.
/// Carries the WFP record + its parent AIP hierarchy so the Excel layer can
/// build the full activity tree without additional DB calls.
/// </summary>
public sealed record WfpExcelReportData(
    WfpRecordDetailDto Wfp,
    AipRecordDetailDto Aip,
    string             OfficeName,
    string             OfficeCode);
