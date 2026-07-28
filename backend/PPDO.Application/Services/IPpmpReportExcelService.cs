using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// Exports a <see cref="PpmpReportDto"/> (RAL-183's item-grained PPMP report, one office + fiscal
/// year) as an .xlsx matching the province's own PPMP working form (<c>ppmp admin2027.xlsx</c>;
/// spec in <c>docs/v1.5/PPMP_Report_Findings.md</c> §8). Named distinctly from the WFP exporters
/// (<see cref="IWfpReportExcelService"/> / <see cref="IWfpExcelService"/>) — do not conflate.
/// Implemented in PPDO.Infrastructure/Services/PpmpReportExcelService.cs using ClosedXML: the
/// sheet is built programmatically from a style catalog, NOT cloned from a reference workbook
/// (the province's files are filled samples with hand-touched formatting, not blank templates —
/// same lesson as v1.4.4's WFP export).
/// </summary>
public interface IPpmpReportExcelService
{
    /// <summary>
    /// Builds the landscape PPMP workbook: one worksheet per fund source, each with the header
    /// block (PPMP title, FY, END-USER/UNIT, "Charged to:"), the item-grained columns from §8
    /// (Mode of Procurement dropped), the Program → Project → Activity → Account section rows,
    /// the procurement item rows with per-quarter qty/amount, account section totals (= sum of
    /// items), a fund grand-total, and the "Prepared by / NOTED BY" signatory block. Values only —
    /// no formulas.
    /// </summary>
    byte[] Export(PpmpReportDto report);
}
