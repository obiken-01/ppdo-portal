namespace PPDO.Application.DTOs.Config;

/// <summary>One office a scoped key may read — display shape only, no internal id (build spec §4.2).</summary>
public sealed record ApiKeyOfficeDto(string Code, string Name);

/// <summary>
/// Row on Configuration → API Access. Slim by construction — <c>KeyHash</c> never leaves
/// <c>PartnerApiKeyService</c> (build spec §4.2: "never the hash").
/// </summary>
public sealed record ApiKeyListItemDto(
    int Id,
    string PartnerName,
    string KeyPrefix,
    bool AllOffices,
    IReadOnlyList<ApiKeyOfficeDto> Offices,
    string Status,
    DateTime? ExpiresAt,
    DateTime? LastUsedAt,
    DateTime CreatedAt,
    string CreatedByName,
    DateTime? RevokedAt,
    string? RevokedByName);

/// <summary>
/// <c>POST /api/config/api-keys</c> body. <see cref="OfficeIds"/> is ignored when
/// <see cref="AllOffices"/> is true. <see cref="ExpiresAt"/> is a plain calendar date (Manila) —
/// the service converts it to the end of that day in UTC.
/// </summary>
public sealed record CreateApiKeyDto(
    string PartnerName,
    bool AllOffices,
    IReadOnlyList<int>? OfficeIds,
    DateOnly? ExpiresAt);

/// <summary>
/// Response to a successful issue. <see cref="PlaintextKey"/> is shown exactly once — the caller
/// (the Configuration → API Access page) must display it and never request it again.
/// </summary>
public sealed record CreateApiKeyResultDto(
    ApiKeyListItemDto Key,
    string PlaintextKey);
