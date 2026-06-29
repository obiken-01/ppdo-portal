using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Users;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// User management — create, read, update, reset password, set permission overrides,
/// soft delete (v1.2 — RAL-97: divisions are now a configurable FK that carries the
/// user's scope AND feature flags; PermissionGroup + the Division enum are retired).
///
///   SuperAdmin → can manage everyone
///   Admin/Staff(CanManageUsers) → can manage Staff only
///
/// Division assignment: Staff require a <c>DivisionId</c>; SuperAdmin/Admin have none.
/// An office user's division must belong to that user's office.
/// </summary>
public sealed class UserService : IUserService
{
    // Default password issued to every newly created user and on reset.
    private const string DefaultPassword = "TamarawUser2026!";

    private readonly IUserRepository _users;
    private readonly IRepository<Office> _offices;
    private readonly IRepository<Division> _divisions;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository users,
        IRepository<Office> offices,
        IRepository<Division> divisions,
        ILogger<UserService> logger)
    {
        _users     = users;
        _offices   = offices;
        _divisions = divisions;
        _logger    = logger;
    }

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserResponseDto>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<User> users = await _users.GetAllWithDivisionAsync(cancellationToken);
        return users.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.GetByIdWithDivisionAsync(id, cancellationToken);
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
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out UserRole newRole))
            return ServiceResult<UserResponseDto>.BadRequest(
                $"'{dto.Role}' is not a valid Role. Valid values: SuperAdmin, Admin, Staff.");

        if (!CanRequesterManageRole(requester, newRole))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a user with role {TargetRole}.",
                requester.Id, newRole);
            return ServiceResult<UserResponseDto>.Forbidden(
                $"You do not have permission to create a user with role '{newRole}'.");
        }

        bool isOfficeUser = dto.OfficeId is int oid && oid > 0;

        if (isOfficeUser && newRole is UserRole.SuperAdmin or UserRole.Admin)
            return ServiceResult<UserResponseDto>.BadRequest(
                "Office users must be Staff, not SuperAdmin/Admin.");

        if (isOfficeUser)
        {
            ServiceResult<UserResponseDto>? officeError =
                await ValidateOfficeAsync(dto.OfficeId!.Value, cancellationToken);
            if (officeError is not null) return officeError;
        }

        // ── Division resolution ───────────────────────────────────────────────
        // SuperAdmin/Admin → no division.
        // Office users (non-PPDO Staff with officeId) → division is optional; office_id scopes them.
        // PPDO Staff (no officeId) → division required.
        int? newDivisionId = null;
        if (newRole is UserRole.Staff && !isOfficeUser)
        {
            if (dto.DivisionId is not int did || did <= 0)
                return ServiceResult<UserResponseDto>.BadRequest("Division is required for Staff users.");

            ServiceResult<UserResponseDto>? divError =
                await ValidateDivisionAsync(did, null, cancellationToken);
            if (divError is not null) return divError;

            newDivisionId = did;
        }
        else if (newRole is UserRole.Staff && isOfficeUser && dto.DivisionId is int offDid && offDid > 0)
        {
            // Optional division for office users — validate it belongs to their office if supplied.
            ServiceResult<UserResponseDto>? divError =
                await ValidateDivisionAsync(offDid, dto.OfficeId, cancellationToken);
            if (divError is not null) return divError;
            newDivisionId = offDid;
        }

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return ServiceResult<UserResponseDto>.BadRequest("FullName is required.");
        if (string.IsNullOrWhiteSpace(dto.Username))
            return ServiceResult<UserResponseDto>.BadRequest("Username is required.");

        User? existingByUsername = await _users.FindByUsernameAsync(dto.Username, cancellationToken);
        if (existingByUsername is not null)
            return ServiceResult<UserResponseDto>.Conflict(
                $"Username '{dto.Username}' is already taken.");

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
            DivisionId   = newDivisionId,
            OfficeId     = isOfficeUser ? dto.OfficeId : null,
            Position     = dto.Position?.Trim(),
            ContactNo    = dto.ContactNo?.Trim(),
            IsActive     = true,
        };

        await _users.AddAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User created. UserId: {UserId}, Role: {Role}, DivisionId: {DivisionId}, CreatedBy: {CreatedBy}",
            user.Id, user.Role, user.DivisionId, requester.Id);

        User created = (await _users.GetByIdWithDivisionAsync(user.Id, cancellationToken))!;
        return ServiceResult<UserResponseDto>.Ok(MapToDto(created));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> UpdateAsync(
        User requester,
        Guid targetId,
        UpdateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        User? target = await _users.GetByIdWithDivisionAsync(targetId, cancellationToken);
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

        if (dto.FullName is not null)  target.FullName  = dto.FullName.Trim();
        if (dto.Position is not null)  target.Position  = dto.Position.Trim();
        if (dto.ContactNo is not null) target.ContactNo = dto.ContactNo.Trim();

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

        if (dto.Email is not null)
        {
            string? newEmail = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant();
            if (!string.Equals(newEmail, target.Email, StringComparison.OrdinalIgnoreCase) && newEmail is not null)
            {
                User? taken = await _users.FindByEmailAsync(newEmail, cancellationToken);
                if (taken is not null)
                    return ServiceResult<UserResponseDto>.Conflict(
                        $"Email '{newEmail}' is already registered.");
            }
            target.Email = newEmail;
        }

        // -- Role ----------------------------------------------------------------
        UserRole effectiveRole = target.Role;
        if (dto.Role is not null)
        {
            if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out UserRole newRole))
                return ServiceResult<UserResponseDto>.BadRequest(
                    $"'{dto.Role}' is not a valid Role. Valid values: SuperAdmin, Admin, Staff.");

            if (!CanRequesterManageRole(requester, newRole))
                return ServiceResult<UserResponseDto>.Forbidden(
                    $"You do not have permission to assign role '{newRole}'.");

            effectiveRole = newRole;
            target.Role   = newRole;
        }

        // -- Office (full replacement; office users have a division within their office) ---
        bool isOfficeUser = dto.OfficeId is int oid && oid > 0;

        if (isOfficeUser && effectiveRole is UserRole.SuperAdmin or UserRole.Admin)
            return ServiceResult<UserResponseDto>.BadRequest(
                "Office users must be Staff, not SuperAdmin/Admin.");

        if (isOfficeUser)
        {
            ServiceResult<UserResponseDto>? officeError =
                await ValidateOfficeAsync(dto.OfficeId!.Value, cancellationToken);
            if (officeError is not null) return officeError;
            target.OfficeId = dto.OfficeId;
        }
        else
        {
            target.OfficeId = null;
        }

        // -- Division ------------------------------------------------------------
        if (effectiveRole is UserRole.SuperAdmin or UserRole.Admin)
        {
            target.DivisionId = null;
        }
        else if (!isOfficeUser)
        {
            // PPDO Staff: division required.
            int? candidateDivisionId = dto.DivisionId ?? target.DivisionId;
            if (candidateDivisionId is not int did || did <= 0)
                return ServiceResult<UserResponseDto>.BadRequest("Division is required for Staff users.");

            ServiceResult<UserResponseDto>? divError =
                await ValidateDivisionAsync(did, null, cancellationToken);
            if (divError is not null) return divError;

            target.DivisionId = did;
        }
        else
        {
            // Office user (non-PPDO Staff): division optional; office_id scopes them.
            // Clear any stale PPDO division that may have been carried over.
            int? candidateDivisionId = dto.DivisionId.HasValue ? dto.DivisionId : null;
            if (candidateDivisionId is int did && did > 0)
            {
                ServiceResult<UserResponseDto>? divError =
                    await ValidateDivisionAsync(did, target.OfficeId, cancellationToken);
                if (divError is not null) return divError;
                target.DivisionId = did;
            }
            else
            {
                target.DivisionId = null;
            }
        }

        // -- Permission overrides (null = inherit from division) -----------------
        target.OverrideCanAccessInventory      = dto.OverrideCanAccessInventory;
        target.OverrideCanAccessReports        = dto.OverrideCanAccessReports;
        target.OverrideCanManageUsers          = dto.OverrideCanManageUsers;
        target.OverrideCanManageResourceLinks  = dto.OverrideCanManageResourceLinks;
        target.OverrideCanAccessBudgetPlanning = dto.OverrideCanAccessBudgetPlanning;
        target.OverrideCanUploadAip            = dto.OverrideCanUploadAip;
        target.OverrideCanManageConfig         = dto.OverrideCanManageConfig;
        target.OverrideCanManageAllocation     = dto.OverrideCanManageAllocation;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User updated. TargetUserId: {TargetUserId}, UpdatedBy: {UpdatedBy}",
            target.Id, requester.Id);

        User updated = (await _users.GetByIdWithDivisionAsync(target.Id, cancellationToken))!;
        return ServiceResult<UserResponseDto>.Ok(MapToDto(updated));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> ResetPasswordAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        User? target = await _users.GetByIdWithDivisionAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        if (!CanRequesterManageTarget(requester, target))
            return ServiceResult<UserResponseDto>.Forbidden(
                "You do not have permission to reset this user's password.");

        target.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(DefaultPassword);
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
        if (requester.Role is not UserRole.SuperAdmin)
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} (Role: {Role}) attempted to set permission overrides for user {TargetUserId}.",
                requester.Id, requester.Role, targetId);
            return ServiceResult<UserResponseDto>.Forbidden(
                "Only SuperAdmin can modify individual permission overrides.");
        }

        User? target = await _users.GetByIdWithDivisionAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {targetId} not found.");

        target.OverrideCanAccessInventory      = dto.OverrideCanAccessInventory;
        target.OverrideCanAccessReports        = dto.OverrideCanAccessReports;
        target.OverrideCanManageUsers          = dto.OverrideCanManageUsers;
        target.OverrideCanManageResourceLinks  = dto.OverrideCanManageResourceLinks;
        target.OverrideCanAccessBudgetPlanning = dto.OverrideCanAccessBudgetPlanning;
        target.OverrideCanUploadAip            = dto.OverrideCanUploadAip;
        target.OverrideCanManageConfig         = dto.OverrideCanManageConfig;
        target.OverrideCanManageAllocation     = dto.OverrideCanManageAllocation;

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
        if (requester.Id == targetId)
            return ServiceResult<UserResponseDto>.BadRequest(
                "You cannot deactivate your own account.");

        User? target = await _users.GetByIdWithDivisionAsync(targetId, cancellationToken);
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
        User? target = await _users.GetByIdWithDivisionAsync(targetId, cancellationToken);
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
            return ServiceResult<UserResponseDto>.BadRequest("User is already active.");

        target.IsActive = true;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User reactivated. TargetUserId: {TargetUserId}, ReactivatedBy: {ReactivatedBy}",
            target.Id, requester.Id);

        return ServiceResult<UserResponseDto>.Ok(MapToDto(target));
    }

    // ── Self-service profile & password ───────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<UserResponseDto>> UpdateOwnProfileAsync(
        User caller,
        UpdateOwnProfileDto dto,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.GetByIdWithDivisionAsync(caller.Id, cancellationToken);
        if (user is null)
            return ServiceResult<UserResponseDto>.NotFound($"User {caller.Id} not found.");

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return ServiceResult<UserResponseDto>.BadRequest("FullName is required.");
        if (string.IsNullOrWhiteSpace(dto.Username))
            return ServiceResult<UserResponseDto>.BadRequest("Username is required.");

        string newUsername = dto.Username.Trim().ToLowerInvariant();
        if (!string.Equals(newUsername, user.Username, StringComparison.OrdinalIgnoreCase))
        {
            User? taken = await _users.FindByUsernameAsync(newUsername, cancellationToken);
            if (taken is not null)
                return ServiceResult<UserResponseDto>.Conflict(
                    $"Username '{newUsername}' is already taken.");
        }

        string? newEmail = string.IsNullOrWhiteSpace(dto.Email)
            ? null
            : dto.Email.Trim().ToLowerInvariant();
        if (!string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase) && newEmail is not null)
        {
            User? taken = await _users.FindByEmailAsync(newEmail, cancellationToken);
            if (taken is not null)
                return ServiceResult<UserResponseDto>.Conflict(
                    $"Email '{newEmail}' is already registered.");
        }

        user.FullName  = dto.FullName.Trim();
        user.Username  = newUsername;
        user.Email     = newEmail;
        user.Position  = dto.Position?.Trim();
        user.ContactNo = dto.ContactNo?.Trim();

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Profile updated. UserId: {UserId}", user.Id);

        User updated = (await _users.GetByIdWithDivisionAsync(user.Id, cancellationToken))!;
        return ServiceResult<UserResponseDto>.Ok(MapToDto(updated));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> ChangePasswordAsync(
        User caller,
        ChangePasswordDto dto,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.GetByIdWithDivisionAsync(caller.Id, cancellationToken);
        if (user is null)
            return ServiceResult<bool>.NotFound($"User {caller.Id} not found.");

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return ServiceResult<bool>.BadRequest("Current password is incorrect.");

        if (dto.NewPassword != dto.ConfirmPassword)
            return ServiceResult<bool>.BadRequest("Passwords do not match.");

        if (dto.NewPassword.Length < 8)
            return ServiceResult<bool>.BadRequest("Password must be at least 8 characters.");
        if (!dto.NewPassword.Any(char.IsUpper))
            return ServiceResult<bool>.BadRequest("Password must contain at least one uppercase letter.");
        if (!dto.NewPassword.Any(char.IsDigit))
            return ServiceResult<bool>.BadRequest("Password must contain at least one digit.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password changed. UserId: {UserId}", user.Id);

        return ServiceResult<bool>.Ok(true);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// True when the requester may create/modify/delete a user whose role is
    /// <paramref name="targetRole"/>. SuperAdmin can manage any role; everyone else
    /// is limited to Staff.
    /// </summary>
    private static bool CanRequesterManageRole(User requester, UserRole targetRole)
    {
        if (requester.Role is UserRole.SuperAdmin)
            return true;
        return targetRole is UserRole.Staff;
    }

    private static bool CanRequesterManageTarget(User requester, User target)
        => CanRequesterManageRole(requester, target.Role);

    /// <summary>
    /// Validates that the office exists and is active. Returns a populated error result
    /// to short-circuit on failure, or null when the office is valid.
    /// </summary>
    private async Task<ServiceResult<UserResponseDto>?> ValidateOfficeAsync(
        int officeId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Office? office = offices.FirstOrDefault(o => o.Id == officeId);

        if (office is null)
            return ServiceResult<UserResponseDto>.BadRequest($"Office {officeId} not found.");
        if (!office.IsActive)
            return ServiceResult<UserResponseDto>.BadRequest($"Office '{office.OfficeName}' is inactive.");

        return null;
    }

    /// <summary>
    /// Validates that the division exists, is active, and (for office users) belongs to the
    /// given office. Returns a populated error result to short-circuit, or null when valid.
    /// </summary>
    private async Task<ServiceResult<UserResponseDto>?> ValidateDivisionAsync(
        int divisionId,
        int? requireOfficeId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Division> divisions = await _divisions.GetAllAsync(cancellationToken);
        Division? division = divisions.FirstOrDefault(d => d.Id == divisionId);

        if (division is null)
            return ServiceResult<UserResponseDto>.BadRequest($"Division {divisionId} not found.");
        if (!division.IsActive)
            return ServiceResult<UserResponseDto>.BadRequest($"Division '{division.Name}' is inactive.");
        if (requireOfficeId is int officeId && division.OfficeId != officeId)
            return ServiceResult<UserResponseDto>.BadRequest(
                $"Division '{division.Name}' does not belong to the selected office.");

        return null;
    }

    /// <summary>Maps a <see cref="User"/> entity (Division navigation must be loaded) to a DTO.</summary>
    private static UserResponseDto MapToDto(User u) => new()
    {
        Id                            = u.Id,
        FullName                      = u.FullName,
        Username                      = u.Username,
        Email                         = u.Email,
        Role                          = u.Role.ToString(),
        DivisionId                    = u.DivisionId,
        Division                      = u.Division?.Name,
        OfficeId                      = u.OfficeId,
        OfficeName                    = u.Office?.OfficeName,
        Position                      = u.Position,
        ContactNo                     = u.ContactNo,
        IsActive                      = u.IsActive,
        OverrideCanAccessInventory    = u.OverrideCanAccessInventory,
        OverrideCanAccessReports      = u.OverrideCanAccessReports,
        OverrideCanManageUsers        = u.OverrideCanManageUsers,
        OverrideCanManageResourceLinks= u.OverrideCanManageResourceLinks,
        OverrideCanAccessBudgetPlanning = u.OverrideCanAccessBudgetPlanning,
        OverrideCanUploadAip            = u.OverrideCanUploadAip,
        OverrideCanManageConfig         = u.OverrideCanManageConfig,
        OverrideCanManageAllocation     = u.OverrideCanManageAllocation,
        CreatedAt                     = u.CreatedAt,
        UpdatedAt                     = u.UpdatedAt,
    };
}
