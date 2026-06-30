namespace PPDO.Application.DTOs.BudgetPlanning;

// ── Read DTOs ─────────────────────────────────────────────────────────────────

public record BudgetCeilingDto(
    int     Id,
    int     OfficeId,
    int     FiscalYear,
    decimal Amount);

public record DivisionAllocationDto(
    int     Id,
    int     DivisionId,
    string  DivisionName,
    int     FiscalYear,
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
    decimal Amount);

public record UpsertAllocationsDto(
    int                                     OfficeId,
    int                                     FiscalYear,
    IReadOnlyList<UpsertDivisionAllocationDto> Allocations);
