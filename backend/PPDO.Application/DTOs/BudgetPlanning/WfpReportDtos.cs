namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// One office eligible for the Report page's office picker (RAL-132) — has at least a Draft
/// WFP for the requested fiscal year. Built by filtering
/// <see cref="IBudgetPlanningDashboardService.GetDashboardAsync"/>'s WfpByOffice rather than a
/// new query, so "eligible" always matches what the Dashboard already shows.
/// </summary>
public record WfpReportOfficeDto(int OfficeId, string OfficeCode, string OfficeName, string WfpStatus);

/// <summary>
/// The money columns shared by an expenditure row and its expense-class/activity roll-ups.
/// AmountToBeReleased always equals NetAppropriation (§2 — reserve is excluded from the
/// quarterly release plan); both are carried because the WFP FINAL sheet has both columns.
/// </summary>
public record WfpReportAmountsDto(
    decimal TotalAppropriation,
    decimal Reserved,
    decimal NetAppropriation,
    decimal Q1,
    decimal Q2,
    decimal Q3,
    decimal Q4,
    decimal AmountToBeReleased)
{
    public static readonly WfpReportAmountsDto Zero = new(0, 0, 0, 0, 0, 0, 0, 0);

    public static WfpReportAmountsDto operator +(WfpReportAmountsDto a, WfpReportAmountsDto b) => new(
        a.TotalAppropriation + b.TotalAppropriation,
        a.Reserved + b.Reserved,
        a.NetAppropriation + b.NetAppropriation,
        a.Q1 + b.Q1, a.Q2 + b.Q2, a.Q3 + b.Q3, a.Q4 + b.Q4,
        a.AmountToBeReleased + b.AmountToBeReleased);
}

/// <summary>
/// One WfpExpenditure, flattened for the report table. Sector is the owning AIP office's
/// sector ("GENERAL PUBLIC SERVICES" / "SOCIAL SERVICES" / "ECONOMIC SERVICES" / "OTHER
/// SERVICES" — AipOffice.Sector, mapped to the WFP FINAL sheet's exact labels), repeated on
/// every row the same way the reference sheet does.
/// </summary>
public record WfpReportRowDto(
    string   Sector,
    string   Nature,
    string?  AccountNumber,
    string?  AccountTitle,
    WfpReportAmountsDto Amounts);

/// <summary>
/// One expense-class subsection under an activity (PERSONAL SERVICES / MAINTENANCE AND OTHER
/// OPERATING EXPENSES / CAPITAL OUTLAY) — only classes with at least one expenditure row are
/// included, matching the "planning is dynamic, not a fixed template" data model.
/// </summary>
public record WfpReportExpenseClassGroupDto(
    string   ExpenseClass,
    string   ExpenseClassLabel,
    IReadOnlyList<WfpReportRowDto> Rows,
    WfpReportAmountsDto SubTotal);

/// <summary>
/// One AIP activity's report block. Rolls up expenditures across ALL of the office's
/// divisions (a WFP record is scoped to one division; the report merges them — see
/// WfpReportService).
/// </summary>
public record WfpReportActivityDto(
    string   RefCode,
    string   Name,
    bool     IsCreation,
    IReadOnlyList<WfpReportExpenseClassGroupDto> ExpenseClasses,
    WfpReportAmountsDto GrandTotal);

/// <summary>PROJECT GRAND TOTAL — sums every activity under the project (the WFP FINAL sheet's row of the same name).</summary>
public record WfpReportProjectDto(
    string   RefCode,
    string   Name,
    IReadOnlyList<WfpReportActivityDto> Activities,
    WfpReportAmountsDto GrandTotal);

/// <summary>PROGRAM GRAND TOTAL — sums every project under the program.</summary>
public record WfpReportProgramDto(
    string   RefCode,
    string   Name,
    IReadOnlyList<WfpReportProjectDto> Projects,
    WfpReportAmountsDto GrandTotal);

/// <summary>
/// The function-band section's closing summary block (WFP FINAL sheet rows "TOTAL - PERSONAL
/// SERVICES" through "GRAND-TOTAL") — every activity in the section split by expense class,
/// with Personal Services and MOOE further split by the activity's IsCreation flag (RAL-126;
/// the checkbox is documented as "GF, PS, position-creation only", so Capital Outlay has no
/// creation split). GrandTotal sums all five buckets and equals the section's overall total.
/// </summary>
public record WfpReportSectionBreakdownDto(
    WfpReportAmountsDto PersonalServices,
    WfpReportAmountsDto MooeExcludingCreation,
    WfpReportAmountsDto CapitalOutlay,
    WfpReportAmountsDto PersonalServicesCreation,
    WfpReportAmountsDto MooeCreation,
    WfpReportAmountsDto GrandTotal);

/// <summary>
/// One function-band section (CORE FUNCTIONS / STRATEGIC FUNCTIONS / SUPPORT FUNCTIONS /
/// UNASSIGNED FUNCTIONS for programs with no band set — RAL-126).
/// </summary>
public record WfpReportFunctionBandSectionDto(
    string   FunctionBand,
    string   FunctionBandLabel,
    IReadOnlyList<WfpReportProgramDto> Programs,
    WfpReportSectionBreakdownDto Breakdown);

/// <summary>
/// One fund source's full report block — the WFP FINAL sheet repeats the ENTIRE header +
/// hierarchy + totals structure once per fund source (e.g. a separate block for "5% GAD Fund"
/// after the "General Fund" block), rather than mixing funds into one table. An activity with
/// expenditures under more than one fund source appears once per fund, each block showing only
/// that fund's rows/totals.
/// </summary>
public record WfpReportFundSourceDto(
    string   FundSourceName,
    IReadOnlyList<WfpReportFunctionBandSectionDto> Sections);

/// <summary>The full WFP report preview for one office + fiscal year (RAL-132).</summary>
public record WfpReportDto(
    int      FiscalYear,
    string   OfficeCode,
    string   OfficeName,
    decimal  ReserveRate,
    IReadOnlyList<WfpReportFundSourceDto> FundSourceReports);
