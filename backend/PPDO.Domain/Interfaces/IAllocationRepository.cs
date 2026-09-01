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

    /// <summary>
    /// Returns every ProgramDivision row for one config office, matched on the
    /// <c>office_id</c> FK (RAL-249). This is the read path — the ref-code overloads above
    /// remain only for the re-link path, where an AIP ref code is all that is known.
    /// </summary>
    Task<IReadOnlyList<ProgramDivision>> GetProgramDivisionsByOfficeIdAsync(
        int officeId, CancellationToken ct = default);

    /// <summary>
    /// Returns the rows for one (config office, program ref code) pair — the write path's
    /// current-state read. Program stays ref-code keyed on purpose; see
    /// <see cref="ProgramDivision"/>'s remarks.
    /// </summary>
    Task<IReadOnlyList<ProgramDivision>> FindProgramDivisionsByOfficeIdAsync(
        int officeId, string programRefCode, CancellationToken ct = default);
}
