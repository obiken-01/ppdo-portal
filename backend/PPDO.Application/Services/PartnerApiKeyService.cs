using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Issues, lists and revokes partner API keys (v1.8.0 — PPDO-15, build spec §3.2 and §5).
/// The caller has already checked <see cref="IPermissionService.CanManageApiKeysAsync"/> — this
/// service owns the rest: name/scope/expiry validation, key generation, and the revoke state
/// machine. See <see cref="ApiKeyGenerator"/> for the key format and hashing.
/// </summary>
public sealed class PartnerApiKeyService : IPartnerApiKeyService
{
    private readonly IPartnerApiKeyRepository _keys;
    private readonly IOfficeRepository _offices;
    private readonly IAuditService _audit;
    private readonly ILogger<PartnerApiKeyService> _logger;

    // Manila is UTC+8 — matches the LoadManilaZone() pattern in DeliveryService/PurchaseRequestService.
    private static readonly TimeZoneInfo ManilaZone = LoadManilaZone();

    private static TimeZoneInfo LoadManilaZone()
    {
        try   { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time"); }
    }

    public PartnerApiKeyService(
        IPartnerApiKeyRepository keys,
        IOfficeRepository offices,
        IAuditService audit,
        ILogger<PartnerApiKeyService> logger)
    {
        _keys    = keys;
        _offices = offices;
        _audit   = audit;
        _logger  = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ApiKeyListItemDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PartnerApiKey> keys = await _keys.GetAllWithOfficesAsync(cancellationToken);
        return keys.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CreateApiKeyResultDto>> IssueAsync(
        Guid issuedById, CreateApiKeyDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.PartnerName) || dto.PartnerName.Length > 100)
            return ServiceResult<CreateApiKeyResultDto>.BadRequest(
                "Partner name is required (up to 100 characters).");

        IReadOnlyList<int> requestedOfficeIds = dto.AllOffices
            ? Array.Empty<int>()
            : (dto.OfficeIds ?? Array.Empty<int>());

        if (!dto.AllOffices && requestedOfficeIds.Count == 0)
            return ServiceResult<CreateApiKeyResultDto>.BadRequest(
                "Choose at least one office, or all offices.");

        List<Office> scopedOffices = [];
        if (!dto.AllOffices)
        {
            // The office table is a small provincial-office config list (~20 rows) — the same
            // fetch-all-then-match shape UserService.ValidateOfficeAsync already uses, not a
            // per-request scan of a growing table.
            IReadOnlyList<Office> allOffices = await _offices.GetAllAsync(cancellationToken);
            Dictionary<int, Office> byId = allOffices.ToDictionary(o => o.Id);

            foreach (int officeId in requestedOfficeIds.Distinct())
            {
                if (!byId.TryGetValue(officeId, out Office? office))
                    return ServiceResult<CreateApiKeyResultDto>.BadRequest($"Office {officeId} not found.");
                if (!office.IsActive)
                    return ServiceResult<CreateApiKeyResultDto>.BadRequest(
                        $"Office '{office.OfficeName}' is inactive.");
                scopedOffices.Add(office);
            }
        }

        DateTime? expiresAtUtc = null;
        if (dto.ExpiresAt is DateOnly expiryDate)
        {
            DateOnly manilaToday = DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ManilaZone));

            if (expiryDate < manilaToday)
                return ServiceResult<CreateApiKeyResultDto>.BadRequest("Expiry must be a future date.");

            // End of the chosen Manila day, converted to UTC, so the key is valid through that
            // whole day locally (build spec §5).
            DateTime endOfDayManila = expiryDate.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Unspecified);
            expiresAtUtc = TimeZoneInfo.ConvertTimeToUtc(endOfDayManila, ManilaZone);
        }

        // Collision guard — 58^8 (~1.3e14) combinations makes a real collision vanishingly
        // unlikely, but the check is one indexed lookup and turns "vanishingly unlikely" into
        // "cannot happen" for free.
        ApiKeyGenerator.Generated generated;
        do { generated = ApiKeyGenerator.Generate(); }
        while (await _keys.GetByPrefixAsync(generated.Prefix, cancellationToken) is not null);

        PartnerApiKey key = new()
        {
            PartnerName = dto.PartnerName.Trim(),
            KeyPrefix   = generated.Prefix,
            KeyHash     = generated.KeyHash,
            AllOffices  = dto.AllOffices,
            ExpiresAt   = expiresAtUtc,
            CreatedById = issuedById,
            Offices     = scopedOffices.Select(o => new PartnerApiKeyOffice { OfficeId = o.Id }).ToList(),
        };

        await _keys.AddAsync(key, cancellationToken);
        await _keys.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Partner API key issued. KeyId: {KeyId}, PartnerName: {PartnerName}, IssuedBy: {IssuedBy}",
            key.Id, key.PartnerName, issuedById);

        PartnerApiKey created = (await _keys.GetByIdAsync(key.Id, cancellationToken))!;

        await _audit.LogAsync("partner_api_keys", created.Id, AuditAction.Create,
            oldValues: null,
            newValues: AuditSnapshot(created),
            cancellationToken);

        return ServiceResult<CreateApiKeyResultDto>.Ok(
            new CreateApiKeyResultDto(MapToDto(created), generated.PlaintextKey));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ApiKeyListItemDto>> RevokeAsync(
        Guid revokedById, int keyId, CancellationToken cancellationToken = default)
    {
        PartnerApiKey? key = await _keys.GetByIdAsync(keyId, cancellationToken);
        if (key is null)
            return ServiceResult<ApiKeyListItemDto>.NotFound("API key not found.");

        if (key.RevokedAt is not null)
            return ServiceResult<ApiKeyListItemDto>.Conflict("This key is already revoked.");

        object oldSnapshot = AuditSnapshot(key);

        key.RevokedAt = DateTime.UtcNow;
        key.RevokedById = revokedById;

        await _keys.UpdateAsync(key, cancellationToken);
        await _keys.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Partner API key revoked. KeyId: {KeyId}, RevokedBy: {RevokedBy}", key.Id, revokedById);

        PartnerApiKey updated = (await _keys.GetByIdAsync(key.Id, cancellationToken))!;

        await _audit.LogAsync("partner_api_keys", updated.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: AuditSnapshot(updated),
            cancellationToken);

        return ServiceResult<ApiKeyListItemDto>.Ok(MapToDto(updated));
    }

    // ── Mapping ────────────────────────────────────────────────────────────────

    /// <summary>Maps a <see cref="PartnerApiKey"/> to its list-row DTO. Never touches KeyHash.</summary>
    private static ApiKeyListItemDto MapToDto(PartnerApiKey key) => new(
        Id: key.Id,
        PartnerName: key.PartnerName,
        KeyPrefix: key.KeyPrefix,
        AllOffices: key.AllOffices,
        Offices: key.Offices
            .Where(o => o.Office is not null)
            .Select(o => new ApiKeyOfficeDto(o.Office!.OfficeCode, o.Office.OfficeName))
            .OrderBy(o => o.Code)
            .ToList(),
        Status: key.GetStatus(DateTime.UtcNow).ToString(),
        ExpiresAt: key.ExpiresAt,
        LastUsedAt: key.LastUsedAt,
        CreatedAt: key.CreatedAt,
        CreatedByName: key.CreatedBy?.FullName ?? "—",
        RevokedAt: key.RevokedAt,
        RevokedByName: key.RevokedBy?.FullName);

    /// <summary>
    /// Audit snapshot of the business-relevant fields on a key. Deliberately excludes KeyHash —
    /// never persist it to audit_log, which is read back and displayed in the Recent Activity UI.
    /// </summary>
    private static object AuditSnapshot(PartnerApiKey key) => new
    {
        key.PartnerName,
        key.KeyPrefix,
        key.AllOffices,
        OfficeCodes = key.Offices.Select(o => o.Office?.OfficeCode).Where(c => c is not null),
        key.ExpiresAt,
        key.RevokedAt,
    };
}
