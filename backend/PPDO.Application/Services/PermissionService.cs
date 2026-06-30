using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Resolves effective feature permissions for an authenticated user (v1.2 — RAL-97).
///
///   SuperAdmin → true for everything (full bypass)
///   Admin      → true for every flag EXCEPT special per-user grants (CanManageAllocation)
///   Staff      → Override ?? user.Division.&lt;flag&gt; ?? false
///
/// CanUploadAip is additionally PPDO-only (office users can never hold it).
/// CanManageAllocation is a per-user grant: SuperAdmin → true, else Override ?? false.
/// CanAccessProfile is always true.
///
/// No database access — the <see cref="User"/> must be loaded with <see cref="User.Division"/>
/// included (JwtMiddleware guarantees this). When Division is null (SuperAdmin/Admin, or a
/// not-yet-assigned Staff user) flag lookups fall back to false — harmless for SuperAdmin/Admin
/// because they short-circuit first.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    /// <inheritdoc />
    public Task<bool> CanAccessInventoryAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanAccessInventory ?? user.Division?.CanAccessInventory ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanAccessReportsAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanAccessReports ?? user.Division?.CanAccessReports ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanManageUsersAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanManageUsers ?? user.Division?.CanManageUsers ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanAccessProfileAsync(User user, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> CanManageResourceLinksAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanManageResourceLinks ?? user.Division?.CanManageResourceLinks ?? false);
    }

    // ── Budget Planning ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<bool> CanAccessBudgetPlanningAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanAccessBudgetPlanning ?? user.Division?.CanAccessBudgetPlanning ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanUploadAipAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);

        // PPDO-only: a non-PPDO office user can never upload (the file contains every office's records).
        if (user.OfficeId is not null) return Task.FromResult(false);

        return Task.FromResult(user.OverrideCanUploadAip ?? user.Division?.CanUploadAip ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanManageConfigAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanManageConfig ?? user.Division?.CanManageConfig ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanManageAllocationAsync(User user, CancellationToken cancellationToken = default)
    {
        // Per-user grant only — Admin is NOT auto-granted. SuperAdmin bypasses for support.
        if (user.Role is UserRole.SuperAdmin) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanManageAllocation ?? false);
    }

    /// <summary>SuperAdmin and Admin get all standard feature flags by default.</summary>
    private static bool IsAdminOrAbove(User user)
        => user.Role is UserRole.SuperAdmin or UserRole.Admin;
}
