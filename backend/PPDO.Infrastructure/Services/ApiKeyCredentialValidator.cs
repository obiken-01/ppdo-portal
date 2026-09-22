using System.Security.Cryptography;
using System.Text;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Infrastructure.Services;

/// <summary>
/// Validates an <c>X-Api-Key</c> header against <c>partner_api_keys</c> (v1.8.0 — PPDO-13). The
/// only <see cref="IPartnerCredentialValidator"/> implementation today — see that interface for
/// why it stays behind one.
///
/// Register as scoped in Program.cs, alongside <see cref="JwtValidator"/>.
/// </summary>
public sealed class ApiKeyCredentialValidator : IPartnerCredentialValidator
{
    private const string KeyPrefixMarker = "ppdo_";

    private readonly IPartnerApiKeyRepository _keys;

    public ApiKeyCredentialValidator(IPartnerApiKeyRepository keys)
    {
        _keys = keys;
    }

    /// <inheritdoc />
    public async Task<PartnerApiKey?> ValidateAsync(
        string? apiKeyHeaderValue, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(apiKeyHeaderValue))
                return null;

            string? prefix = ExtractPrefix(apiKeyHeaderValue);
            if (prefix is null)
                return null;

            // Single indexed lookup — never scans partner_api_keys.
            PartnerApiKey? key = await _keys.GetByPrefixAsync(prefix, cancellationToken);
            if (key is null)
                return null;

            // Constant-time: a length- or early-exit timing difference on the hash compare would
            // let a caller learn how many leading hex characters they guessed correctly.
            if (!FixedTimeHashEquals(ApiKeyGenerator.Hash(apiKeyHeaderValue), key.KeyHash))
                return null;

            return key.GetStatus(DateTime.UtcNow) is ApiKeyStatus.Active ? key : null;
        }
        catch
        {
            // Never let a malformed header or a transient failure propagate — the caller always
            // gets the uniform 401, matching JwtValidator's own never-throws contract.
            return null;
        }
    }

    /// <summary>
    /// Returns the 8-character prefix from <c>ppdo_&lt;prefix&gt;_&lt;secret&gt;</c>, or null when
    /// <paramref name="apiKeyHeaderValue"/> doesn't match that shape. Splitting on '_' would break
    /// on a secret that happens to contain one (base64url uses '_' as one of its two special
    /// characters), so this slices by fixed position instead.
    /// </summary>
    private static string? ExtractPrefix(string apiKeyHeaderValue)
    {
        if (!apiKeyHeaderValue.StartsWith(KeyPrefixMarker, StringComparison.Ordinal))
            return null;

        string afterMarker = apiKeyHeaderValue[KeyPrefixMarker.Length..];
        int prefixLength = ApiKeyGenerator.PrefixLength;

        // Needs at least the prefix, the separator, and a non-empty secret.
        if (afterMarker.Length <= prefixLength + 1 || afterMarker[prefixLength] != '_')
            return null;

        return afterMarker[..prefixLength];
    }

    private static bool FixedTimeHashEquals(string computedHex, string storedHex)
    {
        if (computedHex.Length != storedHex.Length)
            return false;

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computedHex), Encoding.ASCII.GetBytes(storedHex));
    }
}
