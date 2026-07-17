using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAllocationRepository"/>.
/// Adds scoped string-keyed reads for program_divisions on top of the generic
/// <see cref="Repository{T}"/> base. BudgetCeiling and DivisionAllocation have their
/// own scoped repositories (<see cref="BudgetCeilingRepository"/>,
/// <see cref="DivisionAllocationRepository"/> — RAL-163).
/// </summary>
public sealed class AllocationRepository : Repository<ProgramDivision>, IAllocationRepository
{
    public AllocationRepository(AppDbContext context) : base(context) { }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProgramDivision>> FindProgramDivisionsAsync(
        string officeRefCode, string programRefCode, CancellationToken ct = default)
        => await _context.Set<ProgramDivision>()
            .Where(pd => pd.OfficeRefCode == officeRefCode && pd.ProgramRefCode == programRefCode)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ProgramDivision>> GetProgramDivisionsByOfficeRefCodesAsync(
        IReadOnlyList<string> officeRefCodes, CancellationToken ct = default)
    {
        if (officeRefCodes.Count == 0) return [];
        return await _context.Set<ProgramDivision>()
            .Where(pd => officeRefCodes.Contains(pd.OfficeRefCode))
            .ToListAsync(ct);
    }
}
