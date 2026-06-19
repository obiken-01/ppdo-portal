using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Users;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// User management — create, read, update, reset password, set permission overrides,
/// soft delete. Enforces the role-scoping rules from Section 7 of the project docs:
///
///   SuperAdmin → can manage everyone
///   Admin/Staff(CanManageUsers) → can manage Staff + Observer only
///
/// Group assignment on user creation follows the fixed seed mapping from CLAUDE.md:
///   Staff + Division → the matching Division Staff group
///   Observer         → Observer Default group
///   Admin/SuperAdmin → no group (GroupId = null)
/// </summary>
public sealed class UserService : IUserService
{
    // Default password issued to every newly created user and on reset.
    // Users should change this on first login via POST /api/auth/change-password.
    private const string DefaultPassword = "TamarawUser2026!";

    // Fixed seed GUIDs from PermissionGroupConfiguration — must never change.
    private static readonly Guid GroupAdminDivisionStaff = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupPlanningStaff      = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid GroupRMStaff            = new("10000000-0000-0000-0000-000000000003");
    private static readonly Guid GroupMISStaff           = new("10000000-0000-0000-0000-000000000004");
    private static readonly Guid GroupSPDStaff           = new("10000000-0000-0000-0000-000000000005");
    private static readonly Guid GroupObserverDefault    = new("10000000-0000-0000-0000-000000000006");
    private static readonly Guid GroupOfficeUserDefault  = new("10000000-0000-0000-0000-000000000007");

    private readonly IUserRepository _users;
    private readonly IRepository<Office> _offices;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository users, IRepository<Office> offices, ILogger<UserService> logger)
    {
        _users   = users;
        _offices = offices;
        _logger  = logger;
    }

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserResponseDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<User> users = await _users.GetAllWithGroupAsync(cancellationToken);
        return users.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.GetByIdWithGroupAsync(id, cancellationToken);
        return user is null
            ? ServiceResult<UserResponseDto>.NotFound($"User {id} not found.")
            : ServiceResult<UserResponseDto>.Ok(MapToDto(user));
    }

    // ── Mutations ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> CreateAsync(
        User requester,
        CreateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        // Parse and validate Role.
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out UserRole newRole))
            return ServiceResult<UserResponseDto>.BadRequest(
                $"'{dto.Role}' is not a valid Role. Valid values: SuperAdmin, Admin, Staff, Observer.");

        // Scope check: requester cannot create a user with a higher/peer role unless SuperAdmin.
        if (!CanRequesterManageRole(requester, newRole))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a user with role {TargetRole}.",
                requester.Id, newRole);
            return ServiceResult<UserResponseDto>.Forbidden(
                $"You do not have permission to create a user with role '{newRole}'.");
        }

        // ── Office vs Division resolution (v1.1) ──────────────────────────────
        bool isOfficeUser = dto.OfficeId is int oid && oid > 0;

        if (isOfficeUser && newRole is UserRole.SuperAdmin or UserRole.Admin)
            return ServiceResult<UserResponseDto>.BadRequest(
                "Office users must be Staff (encoder) or Observer (viewer), not SuperAdmin/Admin.");

        Division? newDivision = null;
        if (isOfficeUser)
        {
            ServiceResult<UserResponseDto>? officeError =
                await ValidateOfficeAsync(dto.OfficeId!.Value, cancellationToken);
            if (officeError is not null) return officeError;
        }
        else
        {
            // PPDO user — Division is optional except for Staff (needs a division group).
            if (!string.IsNullOrWhiteSpace(dto.Division))
            {
                if (!Enum.TryParse<Division>(dto.Division, ignoreCase: true, out Division parsed))
                    return ServiceResult<UserResponseDto>.BadRequest(
                        $"'{dto.Division}' is not a valid Division. Valid values: Admin, Planning, RM, MIS, SPD.");
                newDivision = parsed;
            }

            if (newRole is UserRole.Staff && newDivision is null)
                return ServiceResult<UserResponseDto>.BadRequest(
                    "Division is required for PPDO Staff (or assign an office instead).");
        }

        // Basic field validation.
        if (string.IsNullOrWhiteSpace(dto.FullName))
            return ServiceResult<UserResponseDto>.BadRequest("FullName is required.");
        if (string.IsNullOrWhiteSpace(dto.Username))
            return ServiceResult<UserResponseDto>.BadRequest("Username is required.");

        // Username uniqueness check.
        User? existingByUsername = await _users.FindByUsernameAsync(dto.Username, cancellationToken);
        if (existingByUsername is not null)
            return ServiceResult<UserResponseDto>.Conflict(
                $"Username '{dto.Username}' is already taken.");

        // Email uniqueness check (only when provided).
        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            User? existingByEmail = await _users.FindByEmailAsync(dto.Email, cancellationToken);
            if (existingByEmail is not null)
                return ServiceResult<UserResponseDto>.Conflict(
                    $"Email '{dto.Email}' is already registered.");
        }

        User user = new()
        {
            Id           = Guid.NewGuid(),
            FullName     = dto.FullName.Trim(),
            Username     = dto.Username.Trim().ToLowerInvariant(),
            Email        = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role         = newRole,
            Division     = newDivision,
            OfficeId     = isOfficeUser ? dto.OfficeId : null,
            GroupId      = GroupIdFor(newRole, newDivision, isOfficeUser),
            Position     = dto.Position?.Trim(),
            ContactNo    = dto.ContactNo?.Trim(),
            IsActive     = true,
        };

        await _users.AddAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User created. UserId: {UserId}, Role: {Role}, Division: {Division}, CreatedBy: {CreatedBy}",
            user.Id, user.Role, user.Division, requester.Id);

        // Reload with group navigation for the response DTO.
        User created = (await _users.GetByIdWithGroupAsync(user.Id, cancellationToken))!;
        return ServiceResult<UserResponseDto>.Ok(MapToDto(created));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> UpdateAsync(
        User requester,
        Guid targetId,
        UpdateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        User? target = await _users.GetByIdWithGroupAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        if (!CanRequesterManageTarget(requester, target))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to update user {TargetUserId} (Role: {TargetRole}).",
                requester.Id, target.Id, target.Role);
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to modify this user.");
        }

        // -- Profile fields ---------------------------------------------------
        if (dto.FullName is not null)
            target.FullName  = dto.FullName.Trim();
        if (dto.Position is not null)
            target.Position  = dto.Position.Trim();
        if (dto.ContactNo is not null)
            target.ContactNo = dto.ContactNo.Trim();

        // -- Username (uniqueness check; null = leave unchanged) ---------------
        if (dto.Username is not null)
        {
            string newUsername = dto.Username.Trim().ToLowerInvariant();
            if (!string.Equals(newUsername, target.Username, StringComparison.OrdinalIgnoreCase))
            {
                User? taken = await _users.FindByUsernameAsync(newUsername, cancellationToken);
                if (taken is not null)
                    return ServiceResult<UserResponseDto>.Conflict(
                        $"Username '{newUsername}' is already taken.");
            }
            target.Username = newUsername;
        }

        // -- Email (uniqueness check; null = leave unchanged) ------------------
        if (dto.Email is not null)
        {
            string? newEmail = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();
            if (!string.Equals(newEmail, target.Email, StringComparison.OrdinalIgnoreCase))
            {
                if (newEmail is not null)
                {
                    User? taken = await _users.FindByEmailAsync(newEmail, cancellationToken);
                    if (taken is not null)
                        return ServiceResult<UserResponseDto>.Conflict(
                            $"Email '{newEmail}' is already registered.");
                }
            }
            target.Email = newEmail;
        }

        // -- Role (triggers GroupId recalculation) ------------------------------
        UserRole effectiveRole = target.Role;

        if (dto.Role is not null)
        {
            if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out UserRole newRole))
                return ServiceResult<UserResponseDto>.BadRequest(
                    $"'{dto.Role}' is not a valid Role. Valid values: SuperAdmin, Admin, Staff, Observer.");

            if (!CanRequesterManageRole(requester, newRole))
                return ServiceResult<UserResponseDto>.Forbidden(
                    $"You do not have permission to assign role '{newRole}'.");

            effectiveRole = newRole;
            target.Role   = newRole;
        }

        // -- Office vs Division (v1.1, full-replacement) ------------------------
        // OfficeId is a full replacement: a positive value makes this a non-PPDO
        // office user (Division cleared); null/0 makes it a PPDO user.
        bool isOfficeUser = dto.OfficeId is int oid && oid > 0;

        if (isOfficeUser && effectiveRole is UserRole.SuperAdmin or UserRole.Admin)
            return ServiceResult<UserResponseDto>.BadRequest(
                "Office users must be Staff (encoder) or Observer (viewer), not SuperAdmin/Admin.");

        if (isOfficeUser)
        {
            ServiceResult<UserResponseDto>? officeError =
                await ValidateOfficeAsync(dto.OfficeId!.Value, cancellationToken);
            if (officeError is not null) return officeError;

            target.OfficeId = dto.OfficeId;
            target.Division = null;   // office users have no division
        }
        else
        {
            target.OfficeId = null;

            if (dto.Division is not null)
            {
                if (!Enum.TryParse<Division>(dto.Division, ignoreCase: true, out Division newDivision))
                    return ServiceResult<UserResponseDto>.BadRequest(
                        $"'{dto.Division}' is not a valid Division. Valid values: Admin, Planning, RM, MIS, SPD.");
                target.Division = newDivision;
            }
        }

        // Auto-recalculate GroupId from the effective role + division/office,
        // unless the caller supplied an explicit GroupId override.
        if (dto.GroupId is not null)
            target.GroupId = dto.GroupId;
        else
            target.GroupId = GroupIdFor(effectiveRole, target.Division, isOfficeUser);

        // -- Permission overrides (null = inherit from group) ------------------
        // Only meaningful for Staff / Observer; Admin/SuperAdmin ignore flags at
        // runtime, but we store whatever the caller sends for consistency.
        target.OverrideCanAccessInventory      = dto.OverrideCanAccessInventory;
        target.OverrideCanAccessReports        = dto.OverrideCanAccessReports;
        target.OverrideCanManageUsers          = dto.OverrideCanManageUsers;
        target.OverrideCanManageResourceLinks  = dto.OverrideCanManageResourceLinks;
        target.OverrideCanAccessBudgetPlanning = dto.OverrideCanAccessBudgetPlanning;
        target.OverrideCanUploadAip            = dto.OverrideCanUploadAip;
        target.OverrideCanManageConfig         = dto.OverrideCanManageConfig;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User updated. TargetUserId: {TargetUserId}, UpdatedBy: {UpdatedBy}",
            target.Id, requester.Id);

        // Reload with group navigation for the response DTO.
        User updated = (await _users.GetByIdWithGroupAsync(target.Id, cancellationToken))!;
        return ServiceResult<UserResponseDto>.Ok(MapToDto(updated));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> ResetPasswordAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        User? target = await _users.GetByIdWithGroupAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        if (!CanRequesterManageTarget(requester, target))
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to reset this user's password.");

        target.PasswordHash   = BCrypt.Net.BCrypt.HashPassword(DefaultPassword);
        // Invalidate active sessions — user must log in again with the new default password.
        target.RefreshToken       = null;
        target.RefreshTokenExpiry = null;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Password reset. TargetUserId: {TargetUserId}, ResetBy: {ResetBy}",
            target.Id, requester.Id);

        return ServiceResult<UserResponseDto>.Ok(MapToDto(target));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> SetPermissionsAsync(
        User requester,
        Guid targetId,
        SetPermissionsDto dto,
        CancellationToken cancellationToken = default)
    {
        // SuperAdmin only.
        if (requester.Role is not UserRole.SuperAdmin)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} (Role: {Role}) attempted to set permission overrides for user {TargetUserId}.",
                requester.Id, requester.Role, targetId);
            return ServiceResult<UserResponseDto>.Forbidden(
                "Only SuperAdmin can modify individual permission overrides.");
        }

        User? target = await _users.GetByIdWithGroupAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        // Observer can never have manage-type / PPDO-only permissions granted.
        if (target.Role is UserRole.Observer
            && (dto.OverrideCanManageUsers == true
                || dto.OverrideCanManageResourceLinks == true
                || dto.OverrideCanUploadAip == true
                || dto.OverrideCanManageConfig == true))
            return ServiceResult<UserResponseDto>.BadRequest(
                "Observer users cannot be granted CanManageUsers, CanManageResourceLinks, CanUploadAip, or CanManageConfig.");

        target.OverrideCanAccessInventory      = dto.OverrideCanAccessInventory;
        target.OverrideCanAccessReports        = dto.OverrideCanAccessReports;
        target.OverrideCanManageUsers          = dto.OverrideCanManageUsers;
        target.OverrideCanManageResourceLinks  = dto.OverrideCanManageResourceLinks;
        target.OverrideCanAccessBudgetPlanning = dto.OverrideCanAccessBudgetPlanning;
        target.OverrideCanUploadAip            = dto.OverrideCanUploadAip;
        target.OverrideCanManageConfig         = dto.OverrideCanManageConfig;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        return ServiceResult<UserResponseDto>.Ok(MapToDto(target));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> DeactivateAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        // Cannot deactivate yourself.
        if (requester.Id == targetId)
            return ServiceResult<UserResponseDto>.BadRequest(
                "You cannot deactivate your own account.");

        User? target = await _users.GetByIdWithGroupAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        if (!CanRequesterManageTarget(requester, target))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to deactivate user {TargetUserId} (Role: {TargetRole}).",
                requester.Id, target.Id, target.Role);
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to deactivate this user.");
        }

        target.IsActive           = false;
        // Invalidate active sessions immediately.
        target.RefreshToken       = null;
        target.RefreshTokenExpiry = null;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User deactivated. TargetUserId: {TargetUserId}, DeactivatedBy: {DeactivatedBy}",
            target.Id, requester.Id);

        return ServiceResult<UserResponseDto>.Ok(MapToDto(target));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> ReactivateAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        User? target = await _users.GetByIdWithGroupAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        if (!CanRequesterManageTarget(requester, target))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to reactivate user {TargetUserId} (Role: {TargetRole}).",
                requester.Id, target.Id, target.Role);
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to reactivate this user.");
        }

        if (target.IsActive)
            return ServiceResult<UserResponseDto>.BadRequest(
                "User is already active.");

        target.IsActive = true;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User reactivated. TargetUserId: {TargetUserId}, ReactivatedBy: {ReactivatedBy}",
            target.Id, requester.Id);

        return ServiceResult<UserResponseDto>.Ok(MapToDto(target));
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// True when the requester may create/modify/delete a user whose role is
    /// <paramref name="targetRole"/>. SuperAdmin can manage any role;
    /// everyone else is limited to Staff and Observer.
    /// </summary>
    private static bool CanRequesterManageRole(User requester, UserRole targetRole)
    {
        if (requester.Role is UserRole.SuperAdmin)
            return true;
        return targetRole is UserRole.Staff or UserRole.Observer;
    }

    /// <summary>
    /// True when the requester may modify <paramref name="target"/>. SuperAdmin can manage
    /// any user; everyone else is limited to Staff and Observer targets.
    /// </summary>
    private static bool CanRequesterManageTarget(User requester, User target)
        => CanRequesterManageRole(requester, target.Role);

    /// <summary>
    /// Returns the fixed seed GroupId for a user based on Role, Division, and whether they
    /// have an office (v1.1). SuperAdmin and Admin do not belong to a group (returns null).
    ///
    ///   SuperAdmin / Admin        → null
    ///   user with an office       → Office User Default (encoder or viewer)
    ///   PPDO Observer             → Observer Default
    ///   PPDO Staff                → the division group matching their division
    /// </summary>
    private static Guid? GroupIdFor(UserRole role, Division? division, bool hasOffice)
    {
        if (role is UserRole.SuperAdmin or UserRole.Admin)
            return null;

        // Non-PPDO office users (encoder or viewer) all share the Office User Default group.
        if (hasOffice)
            return GroupOfficeUserDefault;

        if (role is UserRole.Observer)
            return GroupObserverDefault;

        // PPDO Staff — assign the group matching the division.
        return division switch
        {
            Division.Admin    => GroupAdminDivisionStaff,
            Division.Planning => GroupPlanningStaff,
            Division.RM       => GroupRMStaff,
            Division.MIS      => GroupMISStaff,
            Division.SPD      => GroupSPDStaff,
            _                 => null,
        };
    }

    /// <summary>
    /// Validates that the office exists and is active. Returns a populated error result
    /// to short-circuit on failure, or null when the office is valid.
    /// </summary>
    private async Task<ServiceResult<UserResponseDto>?> ValidateOfficeAsync(
        int officeId,
        CancellationToken cancellationToken)
    {
        // The offices table is tiny (16 rows) — a full read is cheaper than a keyed query
        // path here, and IRepository.GetByIdAsync only accepts Guid keys.
        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Office? office = offices.FirstOrDefault(o => o.Id == officeId);

        if (office is null)
            return ServiceResult<UserResponseDto>.BadRequest($"Office {officeId} not found.");
        if (!office.IsActive)
            return ServiceResult<UserResponseDto>.BadRequest($"Office '{office.OfficeName}' is inactive.");

        return null;
    }

    /// <summary>Maps a <see cref="User"/> entity (Group navigation must be loaded) to a DTO.</summary>
    private static UserResponseDto MapToDto(User u) => new()
    {
        Id                            = u.Id,
        FullName                      = u.FullName,
        Username                      = u.Username,
        Email                         = u.Email,
        Role                          = u.Role.ToString(),
        Division                      = u.Division?.ToString(),
        OfficeId                      = u.OfficeId,
        OfficeName                    = u.Office?.OfficeName,
        Position                      = u.Position,
        ContactNo                     = u.ContactNo,
        IsActive                      = u.IsActive,
        GroupId                       = u.GroupId,
        GroupName                     = u.Group?.Name,
        OverrideCanAccessInventory    = u.OverrideCanAccessInventory,
        OverrideCanAccessReports      = u.OverrideCanAccessReports,
        OverrideCanManageUsers        = u.OverrideCanManageUsers,
        OverrideCanManageResourceLinks= u.OverrideCanManageResourceLinks,
        OverrideCanAccessBudgetPlanning = u.OverrideCanAccessBudgetPlanning,
        OverrideCanUploadAip            = u.OverrideCanUploadAip,
        OverrideCanManageConfig         = u.OverrideCanManageConfig,
        CreatedAt                     = u.CreatedAt,
        UpdatedAt                     = u.UpdatedAt,
    };
}
