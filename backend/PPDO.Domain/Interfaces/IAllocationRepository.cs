using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Repository contract for <see cref="ProgramDivision"/> with scoped string-keyed reads.
/// BudgetCeiling and DivisionAllocation have their own scoped repositories —
/// <see cref="IBudgetCeilingRepository"/> and <see cref="IDivisionAllocationRepository"/>
/// (RAL-163) — not plain <see cref="IRepository{T}"/>.
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
