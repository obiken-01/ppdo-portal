using PPDO.Application.DTOs.BudgetPlanning;

namespace PPDO.Application.Services;

/// <summary>
/// Writes the FY2028+ AIP as the province's Annex B workbook (V18-60 / PPDO-84,
/// <c>AIP_Form_Spec.md</c> Part II §13). Implemented in
/// <c>PPDO.Infrastructure/Services/AipFormExcelService.cs</c> with ClosedXML, built programmatically
/// from a style catalogue — never templated from a copy of the province's file (§8).
///
/// <para>
/// ⚠️ <b>A writer, not a calculator.</b> Every row and figure arrives built by
/// <c>AipFormRowBuilder</c>; the writer divides by 1,000 for the form's unit and writes <c>SUM</c>
/// formulas where the province's file has them. It never rounds or uplifts.
/// </para>
/// </summary>
public interface IAipFormExcelService
{
    /// <summary>One sheet per entry in <paramref name="workbook"/>, in order.</summary>
    byte[] Export(AipFormWorkbookDto workbook);
}
