namespace PPDO.Domain.Entities;

/// <summary>
/// Budget allocation for one division, one fiscal year, and one funding source (v1.2 — RAL-99;
/// funding-source dimension added v1.4.3 — RAL-154). Σ(allocations for all divisions of an
/// office in FY, for one funding source) must not exceed the office's <see cref="BudgetCeiling"/>
/// for that same funding source — enforced in AllocationService. Amounts are in PESOS — never
/// multiply by 1000.
/// </summary>
public sealed class DivisionAllocation
{
    public int     Id              { get; set; }
    public int     DivisionId      { get; set; }
    public int     FiscalYear      { get; set; }
    public int     FundingSourceId { get; set; }
    public decimal Amount          { get; set; }

    public Division?      Division      { get; set; }
    public FundingSource? FundingSource { get; set; }
}
