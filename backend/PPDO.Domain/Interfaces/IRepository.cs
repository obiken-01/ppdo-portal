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
}
