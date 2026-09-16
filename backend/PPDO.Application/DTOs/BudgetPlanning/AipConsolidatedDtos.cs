namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One row's amounts as the AIP form <b>prints</b> them (PPDO-73, <c>AIP_Review_Spec.md</c> §6.3a).
///
/// ⚠️ <b>Pesos, already rounded and uplifted</b> — never the exact amounts AIP Entry shows. The
/// client renders them through the ordinary thousands formatter, which is why they stay in pesos
/// rather than arriving as thousands: one unit rule for every AIP grid.
/// </summary>
/// <param name="Total">Column (11): printed PS + MOOE + CO — a sum of already rounded figures.</param>
public sealed record AipPrintedAmountsDto(
    decimal Ps,
    decimal Mooe,
    decimal Co,
    decimal Total,
    decimal CcAdaptation,
    decimal CcMitigation);

/// <summary>
/// One row of an Annex B sheet (PPDO-73). Flat and slim: the grid renders these in order, and
/// Phase 5's Excel export writes the same rows.
/// </summary>
/// <param name="Kind"><c>Office</c>, <c>Program</c>, <c>Project</c> or <c>Activity</c> — which of the form's description columns B–E the name sits in.</param>
/// <param name="ActivityId">Set on activity rows only — what the grid opens the review modal with.</param>
/// <param name="WorkflowStatus">Office rows only. Screen-only — the Excel does not print it.</param>
/// <param name="FundingSource">Column (7): the activity's expenditure-line fund codes, joined.</param>
/// <param name="Amounts">
/// Every row that has something to add up. ↩️ <b>Program and project rows carried null until
/// PPDO-98</b>, because the province's file leaves those cells blank; the 2026-09-15 PDC demo asked
/// for the subtotals WFP's report already shows, so a heading now carries the sum of the activities
/// beneath it. Still <b>null rather than zero for a heading with no activity under it</b> — the
/// original reason holds there: a printed ₱0 claims the project was costed at nothing rather than not
/// costed at all.
/// </param>
public sealed record AipConsolidatedRowDto(
    string                Kind,
    string                RefCode,
    string                Name,
    int?                  ActivityId,
    string?               WorkflowStatus,
    string?               EsreCode,
    string?               ImplementingOffice,
    string?               StartDate,
    string?               EndDate,
    string?               ExpectedOutputs,
    string?               FundingSource,
    string?               CcTypologyCode,
    AipPrintedAmountsDto? Amounts);

/// <summary>How many offices of one sector have reached PPDO — the tab counts.</summary>
public sealed record AipConsolidatedSectorCountDto(
    string Sector,
    int    SubmittedOffices,
    int    TotalOffices);

/// <summary>
/// One sector sheet of the consolidated AIP (PPDO-73).
///
/// <para>
/// ⚠️ <b>Partial by design</b> (decision 14): only offices at <c>SubmittedToPpdo</c> or
/// <c>Consolidated</c> are in <see cref="Rows"/> and <see cref="Total"/>. An office not yet with
/// PPDO is left out entirely rather than shown as ₱0; the counts are how its absence stays legible.
/// </para>
/// </summary>
/// <param name="Opened">False when the fiscal year has no AIP record yet — an empty sheet, not an error.</param>
/// <param name="SubmittedOffices">Across every sector — distinct offices, not group rows.</param>
public sealed record AipConsolidatedSheetDto(
    int                                          AipRecordId,
    int                                          FiscalYear,
    string                                       Sector,
    bool                                         Opened,
    int                                          SubmittedOffices,
    int                                          TotalOffices,
    IReadOnlyList<AipConsolidatedSectorCountDto> Sectors,
    IReadOnlyList<AipConsolidatedRowDto>         Rows,
    AipPrintedAmountsDto                         Total);

/// <summary>One sector sheet of the Annex B workbook (PPDO-84) — the same rows the grid shows.</summary>
public sealed record AipFormWorkbookSheetDto(
    string                               Sector,
    IReadOnlyList<AipConsolidatedRowDto> Rows,
    AipPrintedAmountsDto                 Total);

/// <summary>
/// Everything the Annex B workbook prints (V18-60 / PPDO-84, <c>AIP_Form_Spec.md</c> Part II).
///
/// ⚠️ <b>Always four sheets, in <c>AipSector.All</c> order</b> — a sector with no office yet is an
/// empty sheet, so the workbook's shape never depends on the season (decision 3).
/// </summary>
/// <param name="AsOf">The download date in Manila — the "As of" month and the file name.</param>
/// <param name="SubmittedOffices">Distinct offices with PPDO or accepted, across every sector.</param>
/// <param name="TotalOffices">Distinct offices in the record. Fewer submitted than this prints the completeness line.</param>
public sealed record AipFormWorkbookDto(
    int                                     FiscalYear,
    DateOnly                                AsOf,
    int                                     SubmittedOffices,
    int                                     TotalOffices,
    IReadOnlyList<AipFormWorkbookSheetDto>  Sheets)
{
    public string FileName => $"AIP_FY{FiscalYear}_{AsOf:yyyy-MM-dd}.xlsx";
}

/// <summary>A built workbook, ready to send.</summary>
public sealed record AipFormExportFileDto(string FileName, byte[] Content);
