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
    private const string DefaultPassword = "PPDOUser2026!";

    // Fixed seed GUIDs from PermissionGroupConfiguration — must never change.
    private static readonly Guid GroupAdminDivisionStaff = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid GroupPlanningStaff      = new("10000000-0000-0000-0000-000000000002");
    private static readonly Guid GroupRMStaff            = new("10000000-0000-0000-0000-000000000003");
    private static readonly Guid GroupMISStaff           = new("10000000-0000-0000-0000-000000000004");
    private static readonly Guid GroupSPDStaff           = new("10000000-0000-0000-0000-000000000005");
    private static readonly Guid GroupObserverDefault    = new("10000000-0000-0000-0000-000000000006");

    private readonly IUserRepository _users;
    private readonly ILogger<UserService> _logger;

    public UserService(IUserRepository users, ILogger<UserService> logger)
    {
        _users  = users;
        _logger = logger;
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
        // Parse and validate Role / Division strings.
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out UserRole newRole))
            return ServiceResult<UserResponseDto>.BadRequest(
                $"'{dto.Role}' is not a valid Role. Valid values: SuperAdmin, Admin, Staff, Observer.");

        if (!Enum.TryParse<Division>(dto.Division, ignoreCase: true, out Division newDivision))
            return ServiceResult<UserResponseDto>.BadRequest(
                $"'{dto.Division}' is not a valid Division. Valid values: Admin, Planning, RM, MIS, SPD.");

        // Scope check: requester cannot create a user with a higher/peer role unless SuperAdmin.
        if (!CanRequesterManageRole(requester, newRole))
            return ServiceResult<UserResponseDto>.Forbidden(
                $"You do not have permission to create a user with role '{newRole}'.");

        // Email uniqueness check.
        User? existing = await _users.FindByEmailAsync(dto.Email, cancellationToken);
        if (existing is not null)
            return ServiceResult<UserResponseDto>.Conflict(
                $"Email '{dto.Email}' is already registered.");

        // Basic field validation.
        if (string.IsNullOrWhiteSpace(dto.FullName))
            return ServiceResult<UserResponseDto>.BadRequest("FullName is required.");
        if (string.IsNullOrWhiteSpace(dto.Email))
            return ServiceResult<UserResponseDto>.BadRequest("Email is required.");

        User user = new()
        {
            Id           = Guid.NewGuid(),
            FullName     = dto.FullName.Trim(),
            Email        = dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role         = newRole,
            Division     = newDivision,
            GroupId      = GroupIdFor(newRole, newDivision),
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
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to modify this user.");

        if (dto.FullName is not null)
            target.FullName  = dto.FullName.Trim();
        if (dto.Position is not null)
            target.Position  = dto.Position.Trim();
        if (dto.ContactNo is not null)
            target.ContactNo = dto.ContactNo.Trim();

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        return ServiceResult<UserResponseDto>.Ok(MapToDto(target));
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
            return ServiceResult<UserResponseDto>.Forbidden(
                "Only SuperAdmin can modify individual permission overrides.");

        User? target = await _users.GetByIdWithGroupAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        // Observer can never have manage permissions granted.
        if (target.Role is UserRole.Observer
            && (dto.OverrideCanManageUsers == true || dto.OverrideCanManageResourceLinks == true))
            return ServiceResult<UserResponseDto>.BadRequest(
                "Observer users cannot be granted CanManageUsers or CanManageResourceLinks.");

        target.OverrideCanAccessInventory     = dto.OverrideCanAccessInventory;
        target.OverrideCanAccessReports       = dto.OverrideCanAccessReports;
        target.OverrideCanManageUsers         = dto.OverrideCanManageUsers;
        target.OverrideCanManageResourceLinks = dto.OverrideCanManageResourceLinks;

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
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to deactivate this user.");

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
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to reactivate this user.");

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
    /// Returns the fixed seed GroupId for a newly created user based on their Role and Division.
    /// SuperAdmin and Admin do not belong to a group (returns null).
    /// </summary>
    private static Guid? GroupIdFor(UserRole role, Division division)
    {
        if (role is UserRole.SuperAdmin or UserRole.Admin)
            return null;

        if (role is UserRole.Observer)
            return GroupObserverDefault;

        // Staff — assign the group matching the division.
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

    /// <summary>Maps a <see cref="User"/> entity (Group navigation must be loaded) to a DTO.</summary>
    private static UserResponseDto MapToDto(User u) => new()
    {
        Id                            = u.Id,
        FullName                      = u.FullName,
        Email                         = u.Email,
        Role                          = u.Role.ToString(),
        Division                      = u.Division.ToString(),
        Position                      = u.Position,
        ContactNo                     = u.ContactNo,
        IsActive                      = u.IsActive,
        GroupId                       = u.GroupId,
        GroupName                     = u.Group?.Name,
        OverrideCanAccessInventory    = u.OverrideCanAccessInventory,
        OverrideCanAccessReports      = u.OverrideCanAccessReports,
        OverrideCanManageUsers        = u.OverrideCanManageUsers,
        OverrideCanManageResourceLinks= u.OverrideCanManageResourceLinks,
        CreatedAt                     = u.CreatedAt,
        UpdatedAt                     = u.UpdatedAt,
    };
}
