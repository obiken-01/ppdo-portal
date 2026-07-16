using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// Exports a <see cref="WfpReportDto"/> (RAL-132's consolidated report, one office + fiscal
/// year) as an .xlsx matching the PBO's official "WFP FINAL" form layout (v1.4.4, RAL-159).
/// Distinct from <see cref="IWfpExcelService"/>, which exports the older single-WfpRecord
/// report and is unrelated/untouched by this feature — do not conflate the two.
/// Implemented in PPDO.Infrastructure/Services/WfpReportExcelService.cs using ClosedXML.
/// </summary>
public interface IWfpReportExcelService
{
    /// <summary>
    /// Builds the landscape WFP FINAL-style workbook: one worksheet per fund source, each with
    /// the header block, function-band → program → project → activity → expense-class
    /// hierarchy (values only — no formulas), and the closing breakdown block. F–K descriptive
    /// columns are always blank (out of scope for v1.4.4 — see docs/v1.4.4).
    /// </summary>
    byte[] Export(WfpReportDto report);
}
