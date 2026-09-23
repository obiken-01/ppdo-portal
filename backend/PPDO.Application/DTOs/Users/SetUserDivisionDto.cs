namespace PPDO.Application.DTOs.Users;

/// <summary>
/// Request body for <c>PUT /api/office/users/{id}/division</c> (PPDO-135).
/// Null clears the user's division; a positive id sets it. Nothing else about the user is
/// touched — this is deliberately narrower than <c>UpdateUserDto</c>, which a department head
/// holding <c>CanManageOfficeSetup</c> alone must never reach (that would be
/// <c>CanManageUsers</c> in disguise).
/// </summary>
public sealed record SetUserDivisionDto(int? DivisionId);
