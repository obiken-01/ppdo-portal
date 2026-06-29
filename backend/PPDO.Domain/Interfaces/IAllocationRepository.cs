using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="ProgramDivision"/> with scoped string-keyed reads.
/// BudgetCeiling and DivisionAllocation are small tables — use plain
/// <see cref="IRepository{T}"/> for those; in-memory filtering is sufficient.
/// </summary>
public interface IAllocationRepository : IRepository<ProgramDivision>
{
    /// <summary>
    /// Returns ProgramDivision rows for the given (officeRefCode, programRefCode) pair.
    /// Returns an empty list when no assignments exist (unassigned program).
    /// </summary>
    Task<IReadOnlyList<ProgramDivision>> FindProgramDivisionsAsync(
        string officeRefCode, string programRefCode, CancellationToken ct = default);

    /// <summary>
    /// Returns all ProgramDivision rows whose OfficeRefCode is in the supplied list.
    /// Used by GetProgramAssignmentsAsync to bulk-load assignments for one office.
    /// </summary>
    Task<IReadOnlyList<ProgramDivision>> GetProgramDivisionsByOfficeRefCodesAsync(
        IReadOnlyList<string> officeRefCodes, CancellationToken ct = default);
}
