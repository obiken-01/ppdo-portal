using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PPDO.Domain.Common;
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
    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default)
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

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            // V18-71 / PPDO-118. Zero rows matched an UPDATE whose WHERE carried the caller's
            // rowversion, i.e. somebody else saved this row since it was loaded. Same translation
            // seam as the unique-index case below, and for the same reason: Application cannot see
            // EF's exception types.
            //
            // ⚠️ Listed BEFORE the DbUpdateException catch because it derives from it. The filter
            // below would not match a concurrency failure today, but relying on that is one edit
            // away from being wrong, and the failure mode would be a conflict surfacing as a 500.
            throw new ConcurrencyConflictException(
                "The row changed since it was loaded.", ex);
        }
        catch (DbUpdateException ex) when (SqlErrors.IsUniqueViolation(ex))
        {
            // V18-44 / PPDO-50. Application references only Domain — it cannot see
            // DbUpdateException or SqlException, so it has no way to tell a unique-index
            // rejection from any other write failure. Infrastructure knows what SQL 2601/2627
            // means; this is where that knowledge is turned into something Application can act on.
            //
            // ⚠️ This TRANSLATES, it does not handle. A caller that does not catch
            // UniqueConstraintViolationException still fails exactly as it did before — unhandled,
            // and a 500. Only RefCodeAllocator catches it today. Deliberately so: a unique-index
            // violation nobody expected is a bug, and a bug returning a polite message is a bug
            // nobody fixes.
            throw new UniqueConstraintViolationException(
                "A unique constraint rejected this write.", SqlErrors.IndexNameOf(ex), ex);
        }
    }

    /// <inheritdoc />
    public void ExpectRowVersion(IRowVersioned entity, byte[]? rowVersion)
    {
        // Null means "no version supplied" — the PPDO-119 rollout state, where old clients are
        // still allowed through. Doing nothing here leaves the save unguarded, which is exactly
        // what that state means; PPDO-121 removes the callers that can reach it.
        if (rowVersion is null) return;

        // OriginalValue, not CurrentValue. EF builds the UPDATE's WHERE from the ORIGINAL value,
        // and the one it read moments ago in this same request is by definition current — so
        // leaving it alone would make the check pass every time. The version that matters came
        // from the browser and is the one being written here.
        _context.Entry(entity).Property(nameof(IRowVersioned.RowVersion)).OriginalValue = rowVersion;
    }

    /// <inheritdoc />
    public async Task ReloadAsync(IRowVersioned entity, CancellationToken cancellationToken = default)
        => await _context.Entry(entity).ReloadAsync(cancellationToken);

    /// <inheritdoc />
    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default)
    {
        // Manual transactions must go through the execution strategy once EnableRetryOnFailure
        // is on, or EF throws — see IRepository<T>.ExecuteInTransactionAsync.
        IExecutionStrategy strategy = _context.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async () =>
        {
            await using IDbContextTransaction transaction =
                await _context.Database.BeginTransactionAsync(cancellationToken);
            try
            {
                await operation();
                await transaction.CommitAsync(cancellationToken);
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        });
    }
}
