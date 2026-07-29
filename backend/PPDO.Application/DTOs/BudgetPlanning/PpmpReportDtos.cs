namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// PPMP report DTOs (v1.5 — RAL-183). Unlike the WFP report (expense-class subsections + a
/// PS/MOOE/CO breakdown), the PPMP is <b>item-grained</b>: it follows the province's own
/// <c>ppmp admin2027.xlsx</c> working form — one row per procurement item, with
/// Program → Project → Activity → Account rendered as interleaved section rows above the items,
/// and a separate table per fund source. Design locked in
/// <c>docs/v1.5/PPMP_Report_Findings.md</c> §8. Procurement-only (Q8); Mode of Procurement
/// dropped (Q12); account section total = sum of the items below it (Q11).
/// </summary>

/// <summary>
/// One procurement line item, grouped across every period it was entered for and spread into the
/// form's four quarter columns. <see cref="Qty"/>/<see cref="EstBudget"/> are the year totals;
/// the QxQty/QxAmount pairs are the per-quarter split (from each source item's PeriodNo mapped to
/// a quarter by the parent expenditure's frequency, matching <c>WfpExpenditureCalculator</c>).
/// StockCardNo/Category are joined <b>live</b> from the Price Index via PriceIndexItemId (Q15) —
/// null for a free-typed item. EstBudget is the sum of the source items' LineTotal (qty × price ×
/// days), so it may differ from Qty × UnitPrice when a day multiplier applies — matching the form.
/// </summary>
public record PpmpReportItemDto(
    string?  StockCardNo,
    string?  Category,
    string   Description,
    string   Unit,
    decimal  UnitPrice,
    decimal  Qty,
    decimal  EstBudget,
    decimal  Q1Qty, decimal Q1Amount,
    decimal  Q2Qty, decimal Q2Amount,
    decimal  Q3Qty, decimal Q3Amount,
    decimal  Q4Qty, decimal Q4Amount);

/// <summary>
/// One account subsection under an activity (a WfpExpenditure's account) with its item rows.
/// <see cref="Total"/> is the SUM of the items' Est. Budget (Q11 — never the AIP appropriation).
/// </summary>
public record PpmpReportAccountDto(
    string?  AccountNumber,
    string?  AccountTitle,
    IReadOnlyList<PpmpReportItemDto> Items,
    decimal  Total);

/// <summary>One AIP activity's block — its account subsections and their combined total.</summary>
public record PpmpReportActivityDto(
    string   RefCode,
    string   Name,
    IReadOnlyList<PpmpReportAccountDto> Accounts,
    decimal  Total);

/// <summary>One AIP project's block — sums every activity under it.</summary>
public record PpmpReportProjectDto(
    string   RefCode,
    string   Name,
    IReadOnlyList<PpmpReportActivityDto> Activities,
    decimal  Total);

/// <summary>One AIP program's block — sums every project under it. (No function-band grouping — the PPMP form lists programs directly, unlike the WFP FINAL sheet.)</summary>
public record PpmpReportProgramDto(
    string   RefCode,
    string   Name,
    IReadOnlyList<PpmpReportProjectDto> Projects,
    decimal  Total);

/// <summary>
/// One fund source's full block ("Charged to: GENERAL FUND"). The PPMP renders a separate table
/// per fund source (Q4), mirroring the WFP report's per-fund grouping — an item under more than
/// one fund appears once per fund, each block showing only that fund's rows/totals.
/// </summary>
public record PpmpReportFundSourceDto(
    string   FundSourceName,
    IReadOnlyList<PpmpReportProgramDto> Programs,
    decimal  Total);

/// <summary>
/// The full PPMP report preview for one office + fiscal year (RAL-183).
/// <see cref="DivisionName"/> is set when the report is scoped to a single division (division
/// users, or a finance officer who picked one) and null for the office-consolidated view — the
/// header's "END-USER/UNIT" line is composed from OfficeName [+ DivisionName].
/// </summary>
public record PpmpReportDto(
    int      FiscalYear,
    string   OfficeCode,
    string   OfficeName,
    string?  DivisionName,
    IReadOnlyList<PpmpReportFundSourceDto> FundSourceReports);
