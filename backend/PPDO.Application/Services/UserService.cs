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
    private readonly IUserRepository _users;
    private readonly IOfficeRepository _offices;
    private readonly IRepository<Division> _divisions;
    private readonly ILogger<UserService> _logger;
    private readonly IAuditService _audit;
    private readonly ILandingPageResolver _landing;

    public UserService(
        IUserRepository users,
        IOfficeRepository offices,
        IRepository<Division> divisions,
        ILogger<UserService> logger,
        IAuditService audit,
        ILandingPageResolver landing)
    {
        _users     = users;
        _offices   = offices;
        _divisions = divisions;
        _logger    = logger;
        _audit     = audit;
        _landing   = landing;
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
    public async Task<ServiceResult<UserCredentialResponseDto>> CreateAsync(
        User requester,
        CreateUserDto dto,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.TryParse<UserRole>(dto.Role, ignoreCase: true, out UserRole newRole))
            return ServiceResult<UserCredentialResponseDto>.BadRequest(
                $"'{dto.Role}' is not a valid Role. Valid values: SuperAdmin, Admin, Staff.");

        if (!CanRequesterManageRole(requester, newRole))
        {
            _logger.LogWarning(
                "Permission denied — user {UserId} attempted to create a user with role {TargetRole}.",
                requester.Id, newRole);
            return ServiceResult<UserCredentialResponseDto>.Forbidden(
                $"You do not have permission to create a user with role '{newRole}'.");
        }

        // "No office selected" in the form means the host office, not the absence of one
        // (DECISION F, RAL-258). Leaving it null would create a user scoped to nothing —
        // before DECISION F the same null meant the opposite, full cross-office access.
        Office? hostOffice = await _offices.GetHostOfficeAsync(cancellationToken);

        // A GUEST-office user is the constrained case. Since every user now has an office,
        // "has an office id" no longer separates anyone — being in a non-host office does.
        bool isOfficeUser = dto.OfficeId is int oid && oid > 0 && oid != hostOffice?.Id;

        if (isOfficeUser && newRole is UserRole.SuperAdmin or UserRole.Admin)
            return ServiceResult<UserCredentialResponseDto>.BadRequest(
                "Office users must be Staff, not SuperAdmin/Admin.");

        if (isOfficeUser)
        {
            ServiceResult<UserCredentialResponseDto>? officeError =
                await ValidateOfficeAsync<UserCredentialResponseDto>(dto.OfficeId!.Value, cancellationToken);
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
                return ServiceResult<UserCredentialResponseDto>.BadRequest("Division is required for Staff users.");

            ServiceResult<UserCredentialResponseDto>? divError =
                await ValidateDivisionAsync<UserCredentialResponseDto>(did, null, cancellationToken);
            if (divError is not null) return divError;

            newDivisionId = did;
        }
        else if (newRole is UserRole.Staff && isOfficeUser && dto.DivisionId is int offDid && offDid > 0)
        {
            // Optional division for office users — validate it belongs to their office if supplied.
            ServiceResult<UserCredentialResponseDto>? divError =
                await ValidateDivisionAsync<UserCredentialResponseDto>(offDid, dto.OfficeId, cancellationToken);
            if (divError is not null) return divError;
            newDivisionId = offDid;
        }

        if (string.IsNullOrWhiteSpace(dto.FullName))
            return ServiceResult<UserCredentialResponseDto>.BadRequest("FullName is required.");
        if (string.IsNullOrWhiteSpace(dto.Username))
            return ServiceResult<UserCredentialResponseDto>.BadRequest("Username is required.");

        User? existingByUsername = await _users.FindByUsernameAsync(dto.Username, cancellationToken);
        if (existingByUsername is not null)
            return ServiceResult<UserCredentialResponseDto>.Conflict(
                $"Username '{dto.Username}' is already taken.");

        if (!string.IsNullOrWhiteSpace(dto.Email))
        {
            User? existingByEmail = await _users.FindByEmailAsync(dto.Email, cancellationToken);
            if (existingByEmail is not null)
                return ServiceResult<UserCredentialResponseDto>.Conflict(
                    $"Email '{dto.Email}' is already registered.");
        }

        // Issued once, shown once — never stored or logged in plaintext (RAL-254).
        string temporaryPassword = PasswordGenerator.Generate();

        User user = new()
        {
            Id           = Guid.NewGuid(),
            FullName     = dto.FullName.Trim(),
            // Stored lower-case so every account matches the office's lowercase convention and
            // relaying credentials never involves spelling out capitals (RAL-254). Matching is
            // separately case-insensitive via the DB collation — see UserRepository.
            Username     = dto.Username.Trim().ToLowerInvariant(),
            Email        = string.IsNullOrWhiteSpace(dto.Email) ? null : dto.Email.Trim().ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
            Role         = newRole,
            DivisionId   = newDivisionId,
            OfficeId     = isOfficeUser ? dto.OfficeId : hostOffice?.Id,
            Office       = isOfficeUser ? null : hostOffice,   // needed by the landing check below
            Position     = dto.Position?.Trim(),
            ContactNo    = dto.ContactNo?.Trim(),
            IsActive     = true,
            // A fresh account starts on the same one-time temporary password an admin
            // reset issues — force the change at next login (RAL-254/RAL-266).
            MustChangePassword = true,
        };

        // Validated against the user as it will exist, not as the requester currently is.
        ServiceResult<UserCredentialResponseDto>? landingError =
            await ValidateLandingPageAsync<UserCredentialResponseDto>(user, dto.LandingPage, cancellationToken);
        if (landingError is not null) return landingError;
        user.LandingPage = ParseLandingPage(dto.LandingPage);

        await _users.AddAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User created. UserId: {UserId}, Role: {Role}, DivisionId: {DivisionId}, CreatedBy: {CreatedBy}",
            user.Id, user.Role, user.DivisionId, requester.Id);

        User created = (await _users.GetByIdWithDivisionAsync(user.Id, cancellationToken))!;
        await _audit.LogAsync("users", created.Id, AuditAction.Create,
            oldValues: null,
            newValues: AuditSnapshot(created),
            cancellationToken);
        return ServiceResult<UserCredentialResponseDto>.Ok(new UserCredentialResponseDto
        {
            User              = MapToDto(created),
            TemporaryPassword = temporaryPassword,
        });
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

        object oldSnapshot = AuditSnapshot(target);

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
        Office? hostOfficeForUpdate = await _offices.GetHostOfficeAsync(cancellationToken);

        // See CreateAsync: only a non-host office makes someone a constrained "office user".
        bool isOfficeUser = dto.OfficeId is int oid && oid > 0 && oid != hostOfficeForUpdate?.Id;

        if (isOfficeUser && effectiveRole is UserRole.SuperAdmin or UserRole.Admin)
            return ServiceResult<UserResponseDto>.BadRequest(
                "Office users must be Staff, not SuperAdmin/Admin.");

        if (isOfficeUser)
        {
            ServiceResult<UserResponseDto>? officeError =
                await ValidateOfficeAsync<UserResponseDto>(dto.OfficeId!.Value, cancellationToken);
            if (officeError is not null) return officeError;
            target.OfficeId = dto.OfficeId;
        }
        else
        {
            // Same rule as CreateAsync: clearing the office means "host office", never "none".
            target.OfficeId = hostOfficeForUpdate?.Id;
            target.Office   = hostOfficeForUpdate;
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
                await ValidateDivisionAsync<UserResponseDto>(did, null, cancellationToken);
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
                    await ValidateDivisionAsync<UserResponseDto>(did, target.OfficeId, cancellationToken);
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
        target.OverrideCanManagePpdoAllocation     = dto.OverrideCanManagePpdoAllocation;
        target.OverrideCanManagePboCeiling      = dto.OverrideCanManagePboCeiling;
        target.OverrideCanReviewBudgetPlanning  = dto.OverrideCanReviewBudgetPlanning;
        target.OverrideCanReviewAllOffices      = dto.OverrideCanReviewAllOffices;

        // Runs after role/division/office and the override flags are applied, so
        // reachability is judged on what the user is about to become.
        ServiceResult<UserResponseDto>? landingError =
            await ValidateLandingPageAsync<UserResponseDto>(target, dto.LandingPage, cancellationToken);
        if (landingError is not null) return landingError;
        target.LandingPage = ParseLandingPage(dto.LandingPage);

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "User updated. TargetUserId: {TargetUserId}, UpdatedBy: {UpdatedBy}",
            target.Id, requester.Id);

        User updated = (await _users.GetByIdWithDivisionAsync(target.Id, cancellationToken))!;
        await _audit.LogAsync("users", updated.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: AuditSnapshot(updated),
            cancellationToken);
        return ServiceResult<UserResponseDto>.Ok(MapToDto(updated));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserCredentialResponseDto>> ResetPasswordAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default)
    {
        User? target = await _users.GetByIdWithDivisionAsync(targetId, cancellationToken);
        if (target is null)
            return ServiceResult<UserCredentialResponseDto>.NotFound($"User {targetId} not found.");

        if (!CanRequesterManageTarget(requester, target))
            return ServiceResult<UserCredentialResponseDto>.Forbidden(
                "You do not have permission to reset this user's password.");

        // Issued once, shown once — never stored or logged in plaintext (RAL-254).
        string temporaryPassword = PasswordGenerator.Generate();

        target.PasswordHash               = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
        target.RefreshToken               = null;
        target.RefreshTokenExpiry         = null;
        // Force a real change at next login (RAL-254's own scope — never wired up until now)
        // and surface the "your password was reset" notice (RAL-267). A fresh reset always
        // needs re-acknowledging, even if a previous one was already dismissed.
        target.MustChangePassword         = true;
        target.LastPasswordResetAt        = DateTime.UtcNow;
        target.PasswordResetAcknowledgedAt = null;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Password reset. TargetUserId: {TargetUserId}, ResetBy: {ResetBy}",
            target.Id, requester.Id);

        // Never snapshot PasswordHash or the issued password — just record that a reset happened.
        await _audit.LogAsync("users", target.Id, AuditAction.Update,
            oldValues: null,
            newValues: new { PasswordReset = true },
            cancellationToken);

        return ServiceResult<UserCredentialResponseDto>.Ok(new UserCredentialResponseDto
        {
            User              = MapToDto(target),
            TemporaryPassword = temporaryPassword,
        });
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

        object oldSnapshot = AuditSnapshot(target);

        target.OverrideCanAccessInventory      = dto.OverrideCanAccessInventory;
        target.OverrideCanAccessReports        = dto.OverrideCanAccessReports;
        target.OverrideCanManageUsers          = dto.OverrideCanManageUsers;
        target.OverrideCanManageResourceLinks  = dto.OverrideCanManageResourceLinks;
        target.OverrideCanAccessBudgetPlanning = dto.OverrideCanAccessBudgetPlanning;
        target.OverrideCanUploadAip            = dto.OverrideCanUploadAip;
        target.OverrideCanManageConfig         = dto.OverrideCanManageConfig;
        target.OverrideCanManagePpdoAllocation     = dto.OverrideCanManagePpdoAllocation;
        target.OverrideCanManagePboCeiling      = dto.OverrideCanManagePboCeiling;
        target.OverrideCanReviewBudgetPlanning  = dto.OverrideCanReviewBudgetPlanning;
        target.OverrideCanReviewAllOffices      = dto.OverrideCanReviewAllOffices;

        await _users.UpdateAsync(target, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync("users", target.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: AuditSnapshot(target),
            cancellationToken);

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

        // Mirrors the soft-delete audit convention used by Division/Account/Office services.
        await _audit.LogAsync("users", target.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);

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

        await _audit.LogAsync("users", target.Id, AuditAction.Update,
            oldValues: new { IsActive = false },
            newValues: new { IsActive = true },
            cancellationToken);

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

        // Self-service: role/division/office are untouched here, so the user's own
        // permissions decide what they may pick.
        ServiceResult<UserResponseDto>? landingError =
            await ValidateLandingPageAsync<UserResponseDto>(user, dto.LandingPage, cancellationToken);
        if (landingError is not null) return landingError;

        object oldSnapshot = AuditSnapshot(user);

        user.FullName    = dto.FullName.Trim();
        user.Username    = newUsername;
        user.Email       = newEmail;
        user.Position    = dto.Position?.Trim();
        user.ContactNo   = dto.ContactNo?.Trim();
        user.LandingPage = ParseLandingPage(dto.LandingPage);

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Profile updated. UserId: {UserId}", user.Id);

        // Self-service, but still a write to `users` — and username and email are identity,
        // not decoration (RAL-246). Role, division and office are untouched here, so this row
        // records who someone became, not what they were allowed to do.
        await _audit.LogAsync("users", user.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: AuditSnapshot(user),
            cancellationToken);

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

        user.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        // Whatever put this user on a temporary password (admin reset or self-service
        // recovery) is satisfied the moment they successfully change it themselves.
        user.MustChangePassword = false;

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password changed. UserId: {UserId}", user.Id);

        // Same shape as ResetPasswordAsync: record THAT it happened, never the hash or the
        // password (RAL-246). Without this row a self-change is indistinguishable from no
        // change at all, which is the gap the reset notice (RAL-267) is meant to close.
        await _audit.LogAsync("users", user.Id, AuditAction.Update,
            oldValues: null,
            newValues: new { PasswordChanged = true },
            cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    // ── Recovery-answer setup (RAL-266) ─────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> SetRecoveryAnswerAsync(
        User caller,
        SetRecoveryAnswerDto dto,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.GetByIdAsync(caller.Id, cancellationToken);
        if (user is null)
            return ServiceResult<bool>.NotFound($"User {caller.Id} not found.");

        if (!RecoveryQuestionName.TryParse(dto.QuestionKey, out RecoveryQuestion question))
            return ServiceResult<bool>.BadRequest(
                $"'{dto.QuestionKey}' is not a valid recovery question. Valid values: {RecoveryQuestionName.ValidValues}.");

        if (string.IsNullOrWhiteSpace(dto.Answer))
            return ServiceResult<bool>.BadRequest("Answer is required.");

        // Same normalize-then-hash path RAL-265 verifies against — a divergence here would
        // silently lock the user out of their own answer.
        string normalized = RecoveryAnswerNormalizer.Normalize(dto.Answer);
        RecoveryQuestion? previousQuestion = user.RecoveryQuestionKey;
        user.RecoveryQuestionKey  = question;
        user.RecoveryAnswerHash   = BCrypt.Net.BCrypt.HashPassword(normalized);
        // Re-running this (changing your answer later) starts the lockout window clean.
        user.RecoveryAttemptCount = 0;
        user.RecoveryFirstAttemptAt = null;

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Recovery answer set. UserId: {UserId}", user.Id);

        // The recovery answer is a credential: it is what self-service reset (RAL-265) checks
        // to hand out a new password. Changing it changes who can take the account over, so it
        // is exactly the class of write RAL-246 exists for — and it was the only one leaving no
        // trace at all. The QUESTION is recorded; the ANSWER HASH never is.
        await _audit.LogAsync("users", user.Id, AuditAction.Update,
            oldValues: new { RecoveryQuestionKey = previousQuestion?.ToString() },
            newValues: new { RecoveryQuestionKey = question.ToString(), RecoveryAnswerChanged = true },
            cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> AcknowledgePasswordResetAsync(
        User caller,
        CancellationToken cancellationToken = default)
    {
        User? user = await _users.GetByIdAsync(caller.Id, cancellationToken);
        if (user is null)
            return ServiceResult<bool>.NotFound($"User {caller.Id} not found.");

        user.PasswordResetAcknowledgedAt = DateTime.UtcNow;

        await _users.UpdateAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        return ServiceResult<bool>.Ok(true);
    }

    // ── Landing page (RAL-262) ────────────────────────────────────────────────

    /// <summary>
    /// Parses the landing-page name and checks the user can actually reach it.
    ///
    /// Validating here matters: a landing page the user cannot open does not fail at
    /// redirect time, it loops — the page ejects them and the redirect sends them back.
    /// The resolver skips unreachable stored values at runtime as a backstop, but silently
    /// ignoring what an admin just saved would be its own kind of wrong.
    /// </summary>
    /// <param name="user">
    /// Must carry the role/office/division the user will have AFTER this operation, not before.
    /// Division is loaded here when needed, since a division change makes a preloaded one stale.
    /// </param>
    private async Task<ServiceResult<TResult>?> ValidateLandingPageAsync<TResult>(
        User user,
        string? landingPageName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(landingPageName))
            return null;

        if (!LandingPageName.TryParse(landingPageName, out LandingPage? parsed) || parsed is not LandingPage page)
            return ServiceResult<TResult>.BadRequest(
                $"'{landingPageName}' is not a valid landing page. Valid values: {LandingPageName.ValidValues}.");

        await EnsureDivisionLoadedAsync(user, cancellationToken);

        if (!await _landing.IsReachableAsync(user, page, cancellationToken))
            return ServiceResult<TResult>.BadRequest(
                $"This user cannot access '{page}', so it cannot be their landing page.");

        return null;
    }

    /// <summary>Parses an already-validated landing-page name. Null/blank clears the preference.</summary>
    private static LandingPage? ParseLandingPage(string? name)
    {
        LandingPageName.TryParse(name, out LandingPage? page);
        return page;
    }

    /// <summary>
    /// Attaches the Division matching <c>user.DivisionId</c> when it is missing or stale.
    /// Permission resolution reads flags off it, so a wrong one silently changes the answer.
    /// </summary>
    private async Task EnsureDivisionLoadedAsync(User user, CancellationToken cancellationToken)
    {
        if (user.DivisionId is not int divisionId)
        {
            user.Division = null;
            return;
        }

        if (user.Division?.Id == divisionId)
            return;

        IReadOnlyList<Division> divisions = await _divisions.GetAllAsync(cancellationToken);
        user.Division = divisions.FirstOrDefault(d => d.Id == divisionId);
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
    private async Task<ServiceResult<TResult>?> ValidateOfficeAsync<TResult>(
        int officeId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Office? office = offices.FirstOrDefault(o => o.Id == officeId);

        if (office is null)
            return ServiceResult<TResult>.BadRequest($"Office {officeId} not found.");
        if (!office.IsActive)
            return ServiceResult<TResult>.BadRequest($"Office '{office.OfficeName}' is inactive.");

        return null;
    }

    /// <summary>
    /// Validates that the division exists, is active, and (for office users) belongs to the
    /// given office. Returns a populated error result to short-circuit, or null when valid.
    /// </summary>
    private async Task<ServiceResult<TResult>?> ValidateDivisionAsync<TResult>(
        int divisionId,
        int? requireOfficeId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<Division> divisions = await _divisions.GetAllAsync(cancellationToken);
        Division? division = divisions.FirstOrDefault(d => d.Id == divisionId);

        if (division is null)
            return ServiceResult<TResult>.BadRequest($"Division {divisionId} not found.");
        if (!division.IsActive)
            return ServiceResult<TResult>.BadRequest($"Division '{division.Name}' is inactive.");
        if (requireOfficeId is int officeId && division.OfficeId != officeId)
            return ServiceResult<TResult>.BadRequest(
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
        LandingPage                   = u.LandingPage?.ToString(),
        OverrideCanAccessInventory    = u.OverrideCanAccessInventory,
        OverrideCanAccessReports      = u.OverrideCanAccessReports,
        OverrideCanManageUsers        = u.OverrideCanManageUsers,
        OverrideCanManageResourceLinks= u.OverrideCanManageResourceLinks,
        OverrideCanAccessBudgetPlanning = u.OverrideCanAccessBudgetPlanning,
        OverrideCanUploadAip            = u.OverrideCanUploadAip,
        OverrideCanManageConfig         = u.OverrideCanManageConfig,
        OverrideCanManagePpdoAllocation     = u.OverrideCanManagePpdoAllocation,
        OverrideCanManagePboCeiling         = u.OverrideCanManagePboCeiling,
        OverrideCanReviewBudgetPlanning     = u.OverrideCanReviewBudgetPlanning,
        OverrideCanReviewAllOffices         = u.OverrideCanReviewAllOffices,
        CreatedAt                     = u.CreatedAt,
        UpdatedAt                     = u.UpdatedAt,
    };

    /// <summary>
    /// Audit snapshot of the business-relevant fields on a user. Deliberately excludes
    /// PasswordHash/RefreshToken/RefreshTokenExpiry — never persist those to audit_log,
    /// which is read back and displayed in the Recent Activity UI.
    /// </summary>
    private static object AuditSnapshot(User u) => new
    {
        u.FullName, u.Username, u.Email, Role = u.Role.ToString(),
        u.DivisionId, u.OfficeId, u.Position, u.ContactNo, u.IsActive,
        u.OverrideCanAccessInventory, u.OverrideCanAccessReports, u.OverrideCanManageUsers,
        u.OverrideCanManageResourceLinks, u.OverrideCanAccessBudgetPlanning,
        u.OverrideCanUploadAip, u.OverrideCanManageConfig, u.OverrideCanManagePpdoAllocation,
        u.OverrideCanManagePboCeiling, u.OverrideCanReviewBudgetPlanning,
        u.OverrideCanReviewAllOffices,
        // RAL-246: LandingPage decides where this user lands; RecoveryQuestionKey identifies
        // WHICH secret can reset the account, and MustChangePassword whether they are on a
        // temporary one. All three are security-relevant state that was being written
        // unrecorded. The two HASHES are still excluded and must stay that way.
        LandingPage = u.LandingPage?.ToString(),
        RecoveryQuestionKey = u.RecoveryQuestionKey?.ToString(),
        u.MustChangePassword,
    };
}
