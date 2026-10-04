using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Users;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// HTTP-triggered Azure Functions for user management (<c>/api/users</c>).
///
/// All endpoints require a valid JWT. The feature-level permission check
/// (<see cref="IPermissionService.CanManageUsersAsync"/>) is applied in every handler
/// before delegating to <see cref="IUserService"/>.
///
/// Exception: <c>PUT /api/users/{id}/permissions</c> is SuperAdmin-only —
/// that additional check is enforced inside <see cref="IUserService.SetPermissionsAsync"/>.
///
/// Second exception: <c>/api/office/users</c> and <c>/api/office/users/{id}/division</c>
/// (PPDO-135) are gated on <see cref="IPermissionService.CanManageOfficeSetupAsync"/> instead —
/// a department head's narrow, own-office-only screen for assigning divisions to their Staff.
/// Deliberately a separate gate rather than widening <c>CanManageUsers</c>: see
/// <c>docs/v1.8/Permission_Matrix.md</c> for why.
/// </summary>
public sealed class UserFunctions
{
    private readonly IUserService _users;
    private readonly IJwtValidator _jwt;
    private readonly IPermissionService _permissions;

    public UserFunctions(
        IUserService users,
        IJwtValidator jwt,
        IPermissionService permissions)
    {
        _users       = users;
        _jwt         = jwt;
        _permissions = permissions;
    }

    // ── GET /api/users ─────────────────────────────────────────────────────────

    [Function("GetUsers")]
    public async Task<HttpResponseData> GetAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        IReadOnlyList<UserResponseDto> result =
            await _users.GetAllAsync(cancellationToken);

        return await OkJson(req, result, cancellationToken);
    }

    // ── GET /api/users/{id} ────────────────────────────────────────────────────

    [Function("GetUserById")]
    public async Task<HttpResponseData> GetById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        ServiceResult<UserResponseDto> result =
            await _users.GetByIdAsync(id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── POST /api/users ────────────────────────────────────────────────────────

    [Function("CreateUser")]
    public async Task<HttpResponseData> Create(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        CreateUserDto? body = await ConfigHttp.ReadBodyAsync<CreateUserDto>(req, cancellationToken, _jsonOptions);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<UserCredentialResponseDto> result =
            await _users.CreateAsync(caller, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.Created, cancellationToken);
    }

    // ── PUT /api/users/{id} ────────────────────────────────────────────────────

    [Function("UpdateUser")]
    public async Task<HttpResponseData> Update(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        UpdateUserDto? body = await ConfigHttp.ReadBodyAsync<UpdateUserDto>(req, cancellationToken, _jsonOptions);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<UserResponseDto> result =
            await _users.UpdateAsync(caller, id, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/users/{id}/reset-password ─────────────────────────────────────

    [Function("ResetUserPassword")]
    public async Task<HttpResponseData> ResetPassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{id:guid}/reset-password")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        ServiceResult<UserCredentialResponseDto> result =
            await _users.ResetPasswordAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/users/{id}/permissions ────────────────────────────────────────

    [Function("SetUserPermissions")]
    public async Task<HttpResponseData> SetPermissions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{id:guid}/permissions")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        // SuperAdmin-only — enforced again inside UserService, but reject early here.
        if (caller.Role is not UserRole.SuperAdmin)
            return req.CreateResponse(HttpStatusCode.Forbidden);

        SetPermissionsDto? body = await ConfigHttp.ReadBodyAsync<SetPermissionsDto>(req, cancellationToken, _jsonOptions);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<UserResponseDto> result =
            await _users.SetPermissionsAsync(caller, id, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── DELETE /api/users/{id} ─────────────────────────────────────────────────

    [Function("DeactivateUser")]
    public async Task<HttpResponseData> Deactivate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "users/{id:guid}")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        ServiceResult<UserResponseDto> result =
            await _users.DeactivateAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/users/{id}/reactivate ─────────────────────────────────────────

    [Function("ReactivateUser")]
    public async Task<HttpResponseData> Reactivate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/{id:guid}/reactivate")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageUsersAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        ServiceResult<UserResponseDto> result =
            await _users.ReactivateAsync(caller, id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── GET /api/users/me ──────────────────────────────────────────────────────

    [Function("GetMe")]
    public async Task<HttpResponseData> GetMe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "users/me")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ServiceResult<UserResponseDto> result =
            await _users.GetByIdAsync(caller.Id, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/users/me ──────────────────────────────────────────────────────

    [Function("UpdateMe")]
    public async Task<HttpResponseData> UpdateMe(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/me")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        UpdateOwnProfileDto? body =
            await ConfigHttp.ReadBodyAsync<UpdateOwnProfileDto>(req, cancellationToken, _jsonOptions);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<UserResponseDto> result =
            await _users.UpdateOwnProfileAsync(caller, body, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── PUT /api/users/me/password ─────────────────────────────────────────────

    [Function("ChangeMyPassword")]
    public async Task<HttpResponseData> ChangeMyPassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/me/password")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        ChangePasswordDto? body =
            await ConfigHttp.ReadBodyAsync<ChangePasswordDto>(req, cancellationToken, _jsonOptions);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<bool> result =
            await _users.ChangePasswordAsync(caller, body, cancellationToken);

        if (result.IsSuccess)
            return req.CreateResponse(HttpStatusCode.NoContent);

        HttpStatusCode status = result.Code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            _                           => HttpStatusCode.InternalServerError,
        };
        HttpResponseData error = req.CreateResponse(status);
        await error.WriteStringAsync(result.Error ?? "An unexpected error occurred.");
        return error;
    }

    // ── PUT /api/users/me/recovery-answer ───────────────────────────────────────

    [Function("SetMyRecoveryAnswer")]
    public async Task<HttpResponseData> SetMyRecoveryAnswer(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "users/me/recovery-answer")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        SetRecoveryAnswerDto? body =
            await ConfigHttp.ReadBodyAsync<SetRecoveryAnswerDto>(req, cancellationToken, _jsonOptions);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<bool> result =
            await _users.SetRecoveryAnswerAsync(caller, body, cancellationToken);

        if (result.IsSuccess)
            return req.CreateResponse(HttpStatusCode.NoContent);

        HttpStatusCode status = result.Code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            _                           => HttpStatusCode.InternalServerError,
        };
        HttpResponseData error = req.CreateResponse(status);
        await error.WriteStringAsync(result.Error ?? "An unexpected error occurred.");
        return error;
    }

    // ── POST /api/users/me/acknowledge-password-reset ──────────────────────────

    [Function("AcknowledgePasswordReset")]
    public async Task<HttpResponseData> AcknowledgePasswordReset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "users/me/acknowledge-password-reset")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        await _users.AcknowledgePasswordResetAsync(caller, cancellationToken);

        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── GET /api/office/users ───────────────────────────────────────────────────
    //
    // PPDO-135 — a department head's own-office user list, for the division-assignment screen.
    // Gated on CanManageOfficeSetup ALONE, deliberately not OR'd with CanManageUsers: granting the
    // latter to reach this would be the privilege escalation the ticket warned against (a
    // department head could then list/edit Staff in every office, PPDO included — see
    // docs/v1.8/Permission_Matrix.md).

    [Function("GetOfficeUsers")]
    public async Task<HttpResponseData> GetOfficeUsers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "office/users")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageOfficeSetupAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        // ⚠️ The caller's OWN office, always — there is no office query parameter to widen here,
        // unlike ConfigDivisionFunctions.List which also serves the (CanManageConfig) admin form.
        // An unassigned caller (DECISION F: null office_id means unassigned) is refused rather than
        // shown an empty list, since CanManageOfficeSetupAsync should never be true without one.
        if (caller.OfficeId is not int officeId)
            return req.CreateResponse(HttpStatusCode.Forbidden);

        IReadOnlyList<OfficeUserDto> result = await _users.GetByOfficeIdAsync(officeId, cancellationToken);
        return await OkJson(req, result, cancellationToken);
    }

    // ── PUT /api/office/users/{id}/division ─────────────────────────────────────
    //
    // PPDO-135's one write: set or clear a Staff member's division. Nothing else about the user —
    // role, permission overrides, password — is reachable through this route. The office axis and
    // the "target must be Staff" rule are enforced inside SetOfficeUserDivisionAsync, not here.

    [Function("SetOfficeUserDivision")]
    public async Task<HttpResponseData> SetOfficeUserDivision(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "office/users/{id:guid}/division")]
        HttpRequestData req,
        Guid id,
        CancellationToken cancellationToken)
    {
        User? caller = await _jwt.ValidateAsync(GetAuthHeader(req), cancellationToken);
        if (caller is null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        if (!await _permissions.CanManageOfficeSetupAsync(caller, cancellationToken))
            return req.CreateResponse(HttpStatusCode.Forbidden);

        SetUserDivisionDto? body =
            await ConfigHttp.ReadBodyAsync<SetUserDivisionDto>(req, cancellationToken, _jsonOptions);
        if (body is null)
            return await BadRequest(req, "Request body is missing or malformed.");

        ServiceResult<OfficeUserDto> result =
            await _users.SetOfficeUserDivisionAsync(caller, id, body.DivisionId, cancellationToken);

        return await ToResponse(req, result, HttpStatusCode.OK, cancellationToken);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? GetAuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    };

    private static async Task<HttpResponseData> OkJson<T>(
        HttpRequestData req,
        T body,
        CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(body, _jsonOptions), cancellationToken);
        return response;
    }

    /// <summary>
    /// Maps a <see cref="ServiceResult{T}"/> to an <see cref="HttpResponseData"/>.
    /// Uses <paramref name="successStatus"/> on success; maps error codes to standard
    /// HTTP status codes on failure.
    /// </summary>
    private static async Task<HttpResponseData> ToResponse<T>(
        HttpRequestData req,
        ServiceResult<T> result,
        HttpStatusCode successStatus,
        CancellationToken cancellationToken)
    {
        if (result.IsSuccess)
        {
            HttpResponseData ok = req.CreateResponse(successStatus);
            ok.Headers.Add("Content-Type", "application/json; charset=utf-8");
            await ok.WriteStringAsync(
                JsonSerializer.Serialize(result.Value, _jsonOptions), cancellationToken);
            return ok;
        }

        HttpStatusCode status = result.Code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.Forbidden  => HttpStatusCode.Forbidden,
            ServiceErrorCode.Conflict   => HttpStatusCode.Conflict,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            _                           => HttpStatusCode.InternalServerError,
        };

        HttpResponseData error = req.CreateResponse(status);
        await error.WriteStringAsync(result.Error ?? "An unexpected error occurred.");
        return error;
    }

    private static async Task<HttpResponseData> BadRequest(
        HttpRequestData req,
        string message)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.BadRequest);
        await response.WriteStringAsync(message);
        return response;
    }
}
