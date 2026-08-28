using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Resolves effective feature permissions for an authenticated user (v1.2 — RAL-97).
///
///   SuperAdmin → true for everything (full bypass)
///   Admin      → true for every flag EXCEPT special per-user grants
///                (CanManagePpdoAllocation, CanManagePboCeiling, CanReviewBudgetPlanning)
///   Staff      → Override ?? user.Division.&lt;flag&gt; ?? false
///
/// CanUploadAip is additionally host-office-only (a guest office can never hold it).
/// CanAccessBudgetPlanning defaults ON for guest-office users — it's their only feature and they
/// have no division to inherit from; an override can still turn it off.
/// CanManagePpdoAllocation is a per-user grant: SuperAdmin → true, else Override ?? false.
/// CanManagePboCeiling is the same shape (RAL-243) but a separate authority — ceiling writes
/// for any office. Holding one never implies the other.
/// CanReviewBudgetPlanning is the same shape again (RAL-244) — the office's reviewer. It is a
/// GRANT: it never denies a write. RAL-256's denial guard is separate and deliberately so.
/// CanAccessProfile is always true.
///
/// No database access — the <see cref="User"/> must be loaded with <see cref="User.Division"/>
/// AND <see cref="User.Office"/> included (JwtMiddleware guarantees both). Division drives the
/// flag lookups; Office carries the host-office flag that the two rules above branch on
/// (DECISION F, RAL-258). When Division is null (SuperAdmin/Admin, or a not-yet-assigned Staff
/// user) flag lookups fall back to false — harmless for SuperAdmin/Admin because they
/// short-circuit first.
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

        // Guest-office users have Budget Planning as their ONLY feature, and they can't be
        // assigned a division in the user form (scoped by office_id, not division). Default their
        // access ON so a division-less office user isn't locked out of the one thing they exist
        // to do; an explicit override can still turn it off.
        if (!OfficeScope.IsHostOfficeUser(user))
            return Task.FromResult(user.OverrideCanAccessBudgetPlanning ?? true);

        // Host-office Staff: inherit from their division.
        return Task.FromResult(user.OverrideCanAccessBudgetPlanning ?? user.Division?.CanAccessBudgetPlanning ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanUploadAipAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);

        // Host-office only: a guest office can never upload (the file contains every office's
        // records).
        if (!OfficeScope.IsHostOfficeUser(user)) return Task.FromResult(false);

        return Task.FromResult(user.OverrideCanUploadAip ?? user.Division?.CanUploadAip ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanManageConfigAsync(User user, CancellationToken cancellationToken = default)
    {
        if (IsAdminOrAbove(user)) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanManageConfig ?? user.Division?.CanManageConfig ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanManagePpdoAllocationAsync(User user, CancellationToken cancellationToken = default)
    {
        // Per-user grant only — Admin is NOT auto-granted. SuperAdmin bypasses for support.
        if (user.Role is UserRole.SuperAdmin) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanManagePpdoAllocation ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanManagePboCeilingAsync(User user, CancellationToken cancellationToken = default)
    {
        // Per-user grant only — Admin is NOT auto-granted. SuperAdmin bypasses for support.
        // Deliberately does NOT fall back to CanManagePpdoAllocation: the two are different
        // authorities (see IPermissionService), and OR-ing them here would quietly hand every
        // PPDO finance officer the power to set other offices' ceilings.
        if (user.Role is UserRole.SuperAdmin) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanManagePboCeiling ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanReviewBudgetPlanningAsync(User user, CancellationToken cancellationToken = default)
    {
        // Per-user grant only — Admin is NOT auto-granted. SuperAdmin bypasses for support.
        // Purely additive: holding this never removes a write the user already had. The
        // reviewer write-denial is RAL-256 and lives in its own guard, not here.
        if (user.Role is UserRole.SuperAdmin) return Task.FromResult(true);
        return Task.FromResult(user.OverrideCanReviewBudgetPlanning ?? false);
    }

    /// <inheritdoc />
    public Task<bool> CanViewAuditLogAsync(User user, CancellationToken cancellationToken = default)
    {
        if (!FeatureFlags.AuditLogPageEnabled) return Task.FromResult(false);
        return Task.FromResult(user.Role is UserRole.SuperAdmin);
    }

    /// <summary>SuperAdmin and Admin get all standard feature flags by default.</summary>
    private static bool IsAdminOrAbove(User user)
        => user.Role is UserRole.SuperAdmin or UserRole.Admin;
}
