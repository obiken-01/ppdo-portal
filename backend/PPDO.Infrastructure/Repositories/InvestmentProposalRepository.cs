using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>EF Core implementation of <see cref="IInvestmentProposalRepository"/> (PPDO-154).</summary>
public sealed class InvestmentProposalRepository : IInvestmentProposalRepository
{
    private readonly AppDbContext _context;

    public InvestmentProposalRepository(AppDbContext context) => _context = context;

    /// <inheritdoc />
    public async Task<InvestmentProposal?> GetByIdAsync(int id, CancellationToken ct = default)
        // Split query: nine collections in one JOIN would multiply every child row by every other.
        // Include depth is 1 throughout.
        => await _context.InvestmentProposals
            .Include(p => p.Beneficiaries)
            .Include(p => p.Benefits)
            .Include(p => p.Logframe)
            .Include(p => p.Groups)
            .Include(p => p.WorkPlanRows)
            .Include(p => p.TeamMembers)
            .Include(p => p.CapacityTrainings)
            .Include(p => p.Monitoring)
            .Include(p => p.Risks)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id, ct);

    /// <inheritdoc />
    public async Task<InvestmentProposal?> GetByProjectIdAsync(int aipProjectId, CancellationToken ct = default)
        => await _context.InvestmentProposals
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AipProjectId == aipProjectId, ct);

    /// <inheritdoc />
    public async Task<bool> ExistsForProjectAsync(int aipProjectId, CancellationToken ct = default)
        => await _context.InvestmentProposals.AnyAsync(p => p.AipProjectId == aipProjectId, ct);

    /// <inheritdoc />
    public async Task AddAsync(InvestmentProposal proposal, CancellationToken ct = default)
        => await _context.InvestmentProposals.AddAsync(proposal, ct);

    /// <inheritdoc />
    public void Remove(InvestmentProposal proposal) => _context.InvestmentProposals.Remove(proposal);

    /// <inheritdoc />
    public void SetExpectedRowVersion(InvestmentProposal proposal, byte[] expectedRowVersion)
        => _context.Entry(proposal).Property(p => p.RowVersion).OriginalValue = expectedRowVersion;

    /// <inheritdoc />
    public async Task SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(
                "The investment proposal was changed by someone else since it was loaded.", ex);
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            // Two tabs creating a proposal for the same project at once: the loser lands here and
            // the service answers 409 with the existing proposal's id.
            throw new UniqueConstraintViolationException(
                "A unique constraint rejected this write.", SqlErrors.IndexNameOf(ex), ex);
        }
    }
}
