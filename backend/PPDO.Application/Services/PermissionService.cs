using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Resolves effective feature permissions for an authenticated user.
///
/// All checks follow the RBAC rules in CLAUDE.md and PROJECT_DOCUMENTATION_NET_AZURE.md:
///
///   SuperAdmin / Admin → true (always — group and override flags are ignored)
///   Staff / Observer   → OverrideXxx ?? Group.Xxx  (null override = inherit group flag)
///
/// Exception — <see cref="CanManageUsersAsync"/> and <see cref="CanManageResourceLinksAsync"/>:
///   Observer is always false regardless of override.
///
/// <see cref="CanAccessProfileAsync"/> is always true for all roles.
///
/// No database access is performed here — the <see cref="User"/> must be loaded with
/// <see cref="User.Group"/> navigation included before calling any method (JwtMiddleware
/// guarantees this). If Group is null (SuperAdmin / Admin edge case), all flag lookups
/// fall back to false, which is harmless because SuperAdmin/Admin short-circuit first.
/// </summary>
public sealed class PermissionService : IPermissionService
{
    /// <inheritdoc />
    public Task<bool> CanAccessInventoryAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return Task.FromResult(true);

        bool groupFlag = user.Group?.CanAccessInventory ?? false;
        bool effective = user.OverrideCanAccessInventory ?? groupFlag;
        return Task.FromResult(effective);
    }

    /// <inheritdoc />
    public Task<bool> CanAccessReportsAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return Task.FromResult(true);

        bool groupFlag = user.Group?.CanAccessReports ?? false;
        bool effective = user.OverrideCanAccessReports ?? groupFlag;
        return Task.FromResult(effective);
    }

    /// <inheritdoc />
    public Task<bool> CanManageUsersAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return Task.FromResult(true);

        // Observer can never manage users — no override can grant this.
        if (user.Role is UserRole.Observer)
            return Task.FromResult(false);

        // Staff: individual override takes precedence; falls back to group flag.
        // All seeded Staff groups have CanManageUsers = false — only an explicit
        // OverrideCanManageUsers = true can grant this to a Staff user.
        bool groupFlag = user.Group?.CanManageUsers ?? false;
        bool effective = user.OverrideCanManageUsers ?? groupFlag;
        return Task.FromResult(effective);
    }

    /// <inheritdoc />
    public Task<bool> CanAccessProfileAsync(User user, CancellationToken cancellationToken = default)
    {
        // All authenticated roles — SuperAdmin, Admin, Staff, Observer — can always
        // view and edit their own profile. No override needed.
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> CanManageResourceLinksAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return Task.FromResult(true);

        // Observer can never manage resource links — no override can grant this.
        if (user.Role is UserRole.Observer)
            return Task.FromResult(false);

        // Staff: individual override takes precedence; falls back to group flag.
        // Note: even when this returns true, Staff may only add links — edit/delete
        // always require Admin or SuperAdmin (enforced in the Resource Links handler).
        bool groupFlag = user.Group?.CanManageResourceLinks ?? false;
        bool effective = user.OverrideCanManageResourceLinks ?? groupFlag;
        return Task.FromResult(effective);
    }

    // ── Budget Planning (v1.1 — RAL-81) ───────────────────────────────────────

    /// <inheritdoc />
    public Task<bool> CanAccessBudgetPlanningAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return Task.FromResult(true);

        // Observer is allowed read-only budget-planning access — no hard block here.
        bool groupFlag = user.Group?.CanAccessBudgetPlanning ?? false;
        bool effective = user.OverrideCanAccessBudgetPlanning ?? groupFlag;
        return Task.FromResult(effective);
    }

    /// <inheritdoc />
    public Task<bool> CanUploadAipAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return Task.FromResult(true);

        // Observer can never upload — no override can grant this.
        if (user.Role is UserRole.Observer)
            return Task.FromResult(false);

        // PPDO-only: a non-PPDO office user (OfficeId set) can never upload, because the
        // uploaded file contains every office's records. No override can grant this.
        if (user.OfficeId is not null)
            return Task.FromResult(false);

        bool groupFlag = user.Group?.CanUploadAip ?? false;
        bool effective = user.OverrideCanUploadAip ?? groupFlag;
        return Task.FromResult(effective);
    }

    /// <inheritdoc />
    public Task<bool> CanManageConfigAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is UserRole.SuperAdmin or UserRole.Admin)
            return Task.FromResult(true);

        // Observer can never manage config — no override can grant this.
        if (user.Role is UserRole.Observer)
            return Task.FromResult(false);

        bool groupFlag = user.Group?.CanManageConfig ?? false;
        bool effective = user.OverrideCanManageConfig ?? groupFlag;
        return Task.FromResult(effective);
    }
}
