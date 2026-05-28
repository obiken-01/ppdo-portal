using Microsoft.EntityFrameworkCore;
using PPDO.Domain.Interfaces;
using PPDO.Infrastructure.Data;

namespace PPDO.Infrastructure.Repositories;

/// <summary>
/// Generic EF Core repository — base implementation of <see cref="IRepository{T}"/>
/// for any domain entity. Feature-specific repositories inherit from this class and
/// add domain-scoped query methods on top.
///
/// Unit of work (SaveChangesAsync) is owned by the calling Application service —
/// never call SaveChanges inside a repository method.
/// Inject <see cref="AppDbContext"/> only here and in derived classes — never directly
/// into Application services or Azure Function handlers.
/// </summary>
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly AppDbContext _context;

    public Repository(AppDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc />
    public async Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Set<T>().FindAsync(new object?[] { id }, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _context.Set<T>().ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task AddAsync(T entity, CancellationToken cancellationToken = default)
        => await _context.Set<T>().AddAsync(entity, cancellationToken);

    /// <inheritdoc />
    public Task UpdateAsync(T entity, CancellationToken cancellationToken = default)
    {
        // EF Core tracks the entity via the change tracker — Update() is synchronous.
        _context.Set<T>().Update(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeleteAsync(T entity, CancellationToken cancellationToken = default)
    {
        // EF Core marks the entity for deletion in the change tracker — Remove() is synchronous.
        _context.Set<T>().Remove(entity);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IQueryable<T> Query()
        => _context.Set<T>().AsQueryable();
}
