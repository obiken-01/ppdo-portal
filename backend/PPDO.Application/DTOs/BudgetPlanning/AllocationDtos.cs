namespace PPDO.Application.DTOs.BudgetPlanning;

// ── Read DTOs ─────────────────────────────────────────────────────────────────

public record BudgetCeilingDto(
    int     Id,
    int     OfficeId,
    int     FiscalYear,
    int     FundingSourceId,
    string  FundingSourceCode,
    string  FundingSourceName,
    decimal Amount);

public record DivisionAllocationDto(
    int     Id,
    int     DivisionId,
    string  DivisionName,
    int     FiscalYear,
    int     FundingSourceId,
    string  FundingSourceCode,
    string  FundingSourceName,
    decimal Amount);

/// <summary>
/// One program row in the PPA → Division assignment tab.
/// DivisionIds is empty when the program has no division assigned ("unassigned").
/// </summary>
public record ProgramAssignmentDto(
    string               OfficeRefCode,
    string               ProgramRefCode,
    string               ProgramName,
    string               Sector,
    IReadOnlyList<int>   DivisionIds);

/// <summary>
/// Gate check result: all three must be true before WFP expenditure entry is allowed
/// for a given (office, FY, division).
/// </summary>
public record AllocationSetupStatusDto(
    bool HasCeiling,
    bool HasAllocation,
    bool HasProgramAssignment);

/// <summary>
/// Office-level (not per-division) allocation-setup counts across all active offices
/// for one fiscal year — used by the "All Offices" dashboard view (RAL-60) where no
/// single office is selected. An office is "fully set up" when it has a ceiling, a
/// positive total division allocation, and at least one assigned program; "not started"
/// when it has none of the three; otherwise "incomplete".
/// </summary>
public record AllocationSetupOverviewDto(
    int TotalOffices,
    int FullySetupCount,
    int IncompleteCount,
    int NotStartedCount);

// ── Write DTOs ────────────────────────────────────────────────────────────────

public record UpsertDivisionAllocationDto(
    int     DivisionId,
    decimal Amount);

public record UpsertProgramAssignmentDto(
    string             OfficeRefCode,
    string             ProgramRefCode,
    IReadOnlyList<int> DivisionIds);

public record UpsertCeilingDto(
    int     OfficeId,
    int     FiscalYear,
    int     FundingSourceId,
    decimal Amount);

public record UpsertAllocationsDto(
    int                                     OfficeId,
    int                                     FiscalYear,
    int                                     FundingSourceId,
    IReadOnlyList<UpsertDivisionAllocationDto> Allocations);
