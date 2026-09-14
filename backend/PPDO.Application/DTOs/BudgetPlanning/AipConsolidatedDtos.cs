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
/// Office and activity rows. ⚠️ <b>Null on program and project rows</b>, which carry no amounts on
/// the form — null rather than zero so a renderer cannot print a zero the province's file leaves blank.
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
