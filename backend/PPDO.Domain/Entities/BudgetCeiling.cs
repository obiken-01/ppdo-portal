namespace PPDO.Domain.Entities;

/// <summary>
/// PBO budget ceiling for one office, one fiscal year, and one funding source (v1.2 — RAL-99;
/// funding-source dimension added v1.4.3 — RAL-154). The ceiling is the maximum total that can
/// be allocated across all divisions for that office+FY+fund. Enforced in
/// AllocationService.UpsertAllocationsAsync. Amounts are in PESOS — never multiply by 1000
/// (that conversion is WFP-layer only).
/// </summary>
public sealed class BudgetCeiling
{
    public int     Id              { get; set; }
    public int     OfficeId        { get; set; }
    public int     FiscalYear      { get; set; }
    public int     FundingSourceId { get; set; }
    public decimal Amount          { get; set; }

    public Office?        Office        { get; set; }
    public FundingSource? FundingSource { get; set; }
}
