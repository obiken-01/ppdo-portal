using PPDO.Domain.Entities;

namespace PPDO.Domain.Interfaces;

/// <summary>
/// Auth-specific user queries used by <c>AuthService</c>.
/// All methods load the <see cref="User.Division"/> navigation (depth 1) so that
/// <see cref="IPermissionService"/> can resolve effective permissions without a second query.
///
/// Extends <see cref="IRepository{User}"/> — inherited methods (GetByIdAsync, UpdateAsync,
/// SaveChangesAsync, etc.) are also available.
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// Returns the active user whose <see cref="User.Username"/> matches (case-insensitive),
    /// with <see cref="User.Division"/> included. Returns null if no match or the user is inactive.
    /// </summary>
    Task<User?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the active user whose <see cref="User.Email"/> matches (case-insensitive),
    /// with <see cref="User.Division"/> included. Returns null if no match or the user
    /// is inactive. Used for email uniqueness checks during user create/update.
    /// </summary>
    Task<User?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user whose <see cref="User.RefreshToken"/> matches exactly,
    /// with <see cref="User.Division"/> included. Returns null if no match.
    /// The caller is responsible for checking <see cref="User.RefreshTokenExpiry"/>.
    /// </summary>
    Task<User?> FindByRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user with the given <paramref name="id"/>, with
    /// <see cref="User.Division"/> and <see cref="User.Office"/> included. Returns null if not found.
    /// Use this instead of the base <see cref="IRepository{T}.GetByIdAsync"/> whenever
    /// division navigation is needed (e.g. permission resolution, user detail responses).
    /// </summary>
    Task<User?> GetByIdWithDivisionAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns all users ordered by <see cref="User.FullName"/>, with
    /// <see cref="User.Division"/> and <see cref="User.Office"/> included.
    /// </summary>
    Task<IReadOnlyList<User>> GetAllWithDivisionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns Id → FullName for the given user ids, computed in SQL (RAL-165 — perf audit
    /// Tier 1). Used by list endpoints (e.g. <c>AipService.GetAllAsync</c>) that need to
    /// resolve a handful of "uploaded by" names without loading the whole users table.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetNamesByIdsAsync(
        IReadOnlyList<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// OfficeId → the name of one active user in that office who holds the budget-planning
    /// reviewer grant (PPDO-20). An office with no such user is absent from the dictionary — that
    /// is the "Cannot submit / None — assign" row on the cross-office dashboard table.
    ///
    /// <b>Matches the stored grant only</b> (<see cref="User.OverrideCanReviewBudgetPlanning"/>),
    /// not the effective permission. <c>PermissionService.CanReviewBudgetPlanningAsync</c> also
    /// answers true for every SuperAdmin, and that bypass is a support capability rather than an
    /// office assignment — resolving it here would name the same administrator as the reviewer of
    /// all fourteen offices, and the column is meant to answer "who in this office can submit".
    /// If the flag ever gains a division- or role-level default, this must resolve it too.
    ///
    /// Where more than one user qualifies, the alphabetically first is returned. The column shows
    /// one name; picking deterministically keeps it from flipping between page loads.
    /// </summary>
    Task<IReadOnlyDictionary<int, string>> GetReviewerNamesByOfficeAsync(
        IReadOnlyList<int> officeIds, CancellationToken cancellationToken = default);
}
