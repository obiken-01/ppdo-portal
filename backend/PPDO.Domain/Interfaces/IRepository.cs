using PPDO.Domain.Common;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Generic repository contract for basic CRUD operations.
/// Feature-specific repositories (e.g. IPurchaseRequestRepository) extend this
/// interface to add domain-specific query methods.
///
/// Implementations live in PPDO.Infrastructure/Repositories/.
/// Never inject or use AppDbContext directly in Application services or Functions —
/// always go through a repository interface.
/// </summary>
/// <typeparam name="T">Domain entity type.</typeparam>
public interface IRepository<T> where T : class
{
    /// <summary>Returns the entity with the given primary key, or null if not found.</summary>
    Task<T?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Returns all entities of this type.</summary>
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists a new entity. Does not call SaveChanges — unit of work is owned by the caller.</summary>
    Task AddAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Marks an existing entity as modified. Does not call SaveChanges.</summary>
    Task UpdateAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>Marks an entity for deletion. Does not call SaveChanges.</summary>
    Task DeleteAsync(T entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exposes an IQueryable for building filtered, sorted, or projected queries
    /// in feature-specific repository methods. Use async terminal operators
    /// (ToListAsync, FirstOrDefaultAsync, etc.) in the calling code.
    /// Never use Include chains deeper than 2 levels.
    /// </summary>
    IQueryable<T> Query();

    /// <summary>
    /// Persists all pending changes to the database.
    /// Application services own the unit of work — call this after all mutations
    /// for a single logical operation are complete.
    /// Returns the number of state entries written.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs <paramref name="operation"/> inside a database transaction so multiple
    /// <see cref="SaveChangesAsync"/> calls it makes commit or roll back together —
    /// use when a single logical operation needs more than one save to complete
    /// (e.g. a per-row import loop where each row must flush before computing the
    /// next). A single SaveChangesAsync call is already atomic and doesn't need this.
    /// Composes with EF's transient-fault retry (<c>EnableRetryOnFailure</c>) via
    /// <c>CreateExecutionStrategy</c> — <paramref name="operation"/> may run more than
    /// once if a transient fault triggers a retry, so it must be safe to redo from
    /// scratch (reset any local accumulators at the start of the delegate).
    /// </summary>
    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// Declares which version of <paramref name="entity"/> the caller was working from, so the
    /// next <see cref="SaveChangesAsync"/> includes it in the UPDATE's WHERE clause and fails
    /// with <c>ConcurrencyConflictException</c> if the stored row has moved on since
    /// (V18-71 / PPDO-118).
    ///
    /// <para>
    /// Without this, EF compares against the version it read a moment ago inside this same
    /// request — which is always current, so the check would pass every time and protect nothing.
    /// The value that matters is the one the <b>browser</b> was holding, which arrives on the
    /// request.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <see cref="IRowVersioned"/> keeps this honest at compile time — today
    /// <c>AipActivity</c> and <c>AipExpenditure</c>. Implementing that interface is still not
    /// sufficient on its own: the property must also be mapped with <c>.IsRowVersion()</c>, or EF
    /// treats it as an ordinary column and never puts it in the WHERE clause.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Pass <c>null</c> and the call is a no-op: the save proceeds with no concurrency check.
    /// That is the PPDO-119 rollout state, not the end state — see <c>AIP_Concurrent_Edit_Spec</c>
    /// §8 and PPDO-121.
    /// </para>
    /// </summary>
    void ExpectRowVersion(IRowVersioned entity, byte[]? rowVersion);

    /// <summary>
    /// Re-reads <paramref name="entity"/> from the database, discarding the tracked copy's
    /// pending changes (V18-71 / PPDO-118).
    ///
    /// <para>
    /// ⚠️ <b>Needed because an ordinary re-query will not do it.</b> After a failed
    /// <see cref="SaveChangesAsync"/> the change tracker still holds the entity with the caller's
    /// attempted values. A fresh <c>FirstOrDefaultAsync</c> for the same key runs the SQL but then
    /// identity-resolves back to that same tracked instance and keeps its modified values — so
    /// building a "here is what the row says now" payload from it would show the caller their own
    /// rejected edit back, labelled as somebody else's.
    /// </para>
    /// </summary>
    Task ReloadAsync(IRowVersioned entity, CancellationToken cancellationToken = default);
}
