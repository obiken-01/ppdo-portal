using PPDO.Application.Common;
using PPDO.Application.DTOs.Users;
using PPDO.Domain.Entities;

namespace PPDO.Application.Services;

/// <summary>
/// User management operations.
/// Implemented in <c>UserService.cs</c> in this namespace.
///
/// The caller (Function handler) is responsible for:
///   1. JWT validation (<see cref="IJwtMiddleware.ValidateAsync"/>)
///   2. Feature-level permission check (<see cref="IPermissionService.CanManageUsersAsync"/>)
///
/// This service enforces the scope rules:
///   SuperAdmin   — can manage everyone (SuperAdmin, Admin, Staff)
///   Admin/Staff  — can manage Staff only
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Returns all users ordered by full name, with group navigation populated.
    /// </summary>
    Task<IReadOnlyList<UserResponseDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the user with the given <paramref name="id"/>.
    /// Returns <see cref="ServiceErrorCode.NotFound"/> when the ID does not exist.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new user.
    /// Staff users require a division id (which carries their scope + feature flags);
    /// SuperAdmin/Admin have none. The initial password is the system default.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — requester cannot create a user with the given role.
    ///   <see cref="ServiceErrorCode.Conflict"/>   — email already exists.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — Role or Division string is not a valid enum value.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> CreateAsync(
        User requester,
        CreateUserDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates profile fields (FullName, Position, ContactNo), Role, Division, GroupId,
    /// and individual permission override flags for an existing user.
    /// Changing Role or Division auto-recalculates GroupId unless an explicit GroupId is supplied.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.NotFound"/>   — target user not found.
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — requester cannot manage the target user or assign the given role.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — invalid Role or Division string.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> UpdateAsync(
        User requester,
        Guid targetId,
        UpdateUserDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the target user's password to the system default.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.NotFound"/>   — target user not found.
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — requester cannot manage the target user.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> ResetPasswordAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets all four individual permission overrides on a user (SuperAdmin only).
    /// Null values clear the override, restoring group-level inheritance.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — requester is not SuperAdmin.
    ///   <see cref="ServiceErrorCode.NotFound"/>   — target user not found.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — attempted to grant manage permissions to an Observer.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> SetPermissionsAsync(
        User requester,
        Guid targetId,
        SetPermissionsDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-deletes a user by setting <see cref="User.IsActive"/> to false and
    /// clearing their refresh token (invalidating active sessions).
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.NotFound"/>   — target user not found.
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — requester cannot manage the target user.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — requester cannot deactivate themselves.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> DeactivateAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Restores a previously deactivated user by setting <see cref="User.IsActive"/> to true.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.NotFound"/>   — target user not found.
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — requester cannot manage the target user.
    ///   <see cref="ServiceErrorCode.BadRequest"/>  — user is already active.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> ReactivateAsync(
        User requester,
        Guid targetId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Self-service profile update — updates only the five editable fields
    /// (FullName, Username, Email, Position, ContactNo) for the authenticated user.
    /// Role, Division, OfficeId, permission overrides, and IsActive are never touched.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.NotFound"/>  — caller's user record not found.
    ///   <see cref="ServiceErrorCode.Conflict"/>  — username or email already taken by another user.
    ///   <see cref="ServiceErrorCode.BadRequest"/> — FullName or Username is empty.
    /// </summary>
    Task<ServiceResult<UserResponseDto>> UpdateOwnProfileAsync(
        User caller,
        UpdateOwnProfileDto dto,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Self-service password change. Validates the current password, checks confirmation
    /// and policy (min 8 chars, ≥1 uppercase, ≥1 digit), then hashes and saves the new one.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.NotFound"/>  — caller's user record not found.
    ///   <see cref="ServiceErrorCode.BadRequest"/> — wrong current password, mismatch, or policy failure.
    /// </summary>
    Task<ServiceResult<bool>> ChangePasswordAsync(
        User caller,
        ChangePasswordDto dto,
        CancellationToken cancellationToken = default);
}
