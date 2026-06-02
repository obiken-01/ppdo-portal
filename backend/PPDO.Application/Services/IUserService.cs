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
/// This service enforces the scope rules from Section 7 of the project documentation:
///   SuperAdmin   — can manage everyone (SuperAdmin, Admin, Staff, Observer)
///   Admin/Staff  — can manage Staff + Observer only
///   Observer     — no management at all (caller must reject before reaching this service)
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
    /// The PermissionGroup is auto-assigned from the new user's Role and Division.
    /// The initial password is the system default — the user should change it on first login.
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
    /// Updates FullName, Position, and ContactNo of an existing user.
    ///
    /// Returns:
    ///   <see cref="ServiceErrorCode.NotFound"/>   — target user not found.
    ///   <see cref="ServiceErrorCode.Forbidden"/>  — requester cannot manage the target user.
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
}
