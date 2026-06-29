namespace PPDO.Domain.Entities;

/// <summary>
/// Budget allocation for one division in one fiscal year (v1.2 — RAL-99).
/// Σ(allocations for all divisions of an office in FY) must not exceed the
/// office's <see cref="BudgetCeiling"/> — enforced in AllocationService.
/// Amounts are in PESOS — never multiply by 1000.
/// </summary>
public sealed class DivisionAllocation
{
    public int     Id         { get; set; }
    public int     DivisionId { get; set; }
    public int     FiscalYear { get; set; }
    public decimal Amount     { get; set; }

    public Division? Division { get; set; }
}
