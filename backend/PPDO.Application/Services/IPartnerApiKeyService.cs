using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Issues, lists and revokes partner API keys (v1.8.0 — PPDO-15).
/// Implemented in <c>PartnerApiKeyService.cs</c> in this namespace.
///
/// The caller (Function handler) is responsible for:
///   1. JWT validation
///   2. Feature-level permission check (<see cref="IPermissionService.CanManageApiKeysAsync"/>)
///
/// This service owns the rest of build spec §3.2: scope/name/expiry validation, generating and
/// hashing the key, the revoke state machine, and the audit_log rows for issue/revoke.
/// </summary>
public interface IPartnerApiKeyService
{
    /// <summary>All keys, newest first, for the Configuration → API Access list.</summary>
    Task<IReadOnlyList<ApiKeyListItemDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues a new key for <paramref name="dto"/>. Validates the partner name, the office scope
    /// (build spec §3.2 "No scope"), and the expiry, then generates and stores the key. The
    /// plaintext is returned exactly once in <see cref="CreateApiKeyResultDto.PlaintextKey"/> —
    /// nothing else in this service, or anywhere downstream, can produce it again.
    /// </summary>
    /// <param name="issuedById">The admin issuing the key — stamped as <c>created_by_id</c> and as
    /// the actor on the <c>audit_log</c> CREATE row.</param>
    Task<ServiceResult<CreateApiKeyResultDto>> IssueAsync(
        Guid issuedById, CreateApiKeyDto dto, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes an active key. Returns <see cref="ServiceErrorCode.NotFound"/> when
    /// <paramref name="keyId"/> doesn't exist, or <see cref="ServiceErrorCode.Conflict"/> when it
    /// is already revoked (build spec §3.2 "Revoke again") — revocation is terminal, never a
    /// toggle.
    /// </summary>
    /// <param name="revokedById">The admin revoking the key — stamped as <c>revoked_by_id</c> and
    /// as the actor on the <c>audit_log</c> UPDATE row.</param>
    Task<ServiceResult<ApiKeyListItemDto>> RevokeAsync(
        Guid revokedById, int keyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// One page of <paramref name="keyId"/>'s call history, newest first, for the "Usage" modal
    /// (build spec §4.2/§6.1, 50 per page). <see cref="ServiceErrorCode.NotFound"/> when the key
    /// doesn't exist.
    /// </summary>
    Task<ServiceResult<ApiKeyRequestLogPageDto>> GetRequestsAsync(
        int keyId, int page, CancellationToken cancellationToken = default);
}
