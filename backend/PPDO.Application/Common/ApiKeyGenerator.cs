using System.Security.Cryptography;
using System.Text;

namespace PPDO.Application.Common;

/// <summary>
/// Generates and hashes partner API keys (v1.8.0 — PPDO-15, build spec §2 decision 5).
///
/// Format <c>ppdo_&lt;prefix&gt;_&lt;secret&gt;</c>: an 8-character prefix used to look the key up
/// without touching the hash, and a 32-byte random secret. Only <see cref="Hash"/> of the whole
/// plaintext key is ever stored — SHA-256, not BCrypt, because the secret is already high-entropy
/// (32 random bytes), so a slow hash buys nothing against guessing and costs every external
/// request. <see cref="Hash"/> is reused unchanged by the auth path that checks an incoming key
/// (PPDO-13), so issuing and validating can never drift onto two different hashes of "the same" key.
/// </summary>
public static class ApiKeyGenerator
{
    // Same unambiguous alphabet as PasswordGenerator (no O/0, I/l/1) — the prefix is shown in the
    // admin UI and possibly read aloud when a key is handed over outside email/chat.
    private const string PrefixAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    /// <summary>Length of the public lookup prefix.</summary>
    public const int PrefixLength = 8;

    private const int SecretByteLength = 32;

    /// <summary>One freshly generated key: the prefix (stored plain), the plaintext (shown once,
    /// never stored), and its hash (what actually gets persisted).</summary>
    public readonly record struct Generated(string Prefix, string PlaintextKey, string KeyHash);

    /// <summary>Generates a new key. Callers must treat <see cref="Generated.PlaintextKey"/> as
    /// write-once — return it in the create response and never persist or log it.</summary>
    public static Generated Generate()
    {
        string prefix = GeneratePrefix();
        string secret = GenerateSecret();
        string plaintextKey = $"ppdo_{prefix}_{secret}";
        return new Generated(prefix, plaintextKey, Hash(plaintextKey));
    }

    /// <summary>
    /// SHA-256 of <paramref name="plaintextKey"/> as lower-case hex (64 chars) — the value stored
    /// in <c>partner_api_keys.key_hash</c> and recomputed on every authenticated call to compare
    /// against it.
    /// </summary>
    public static string Hash(string plaintextKey)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(plaintextKey));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GeneratePrefix()
    {
        char[] chars = new char[PrefixLength];
        for (int i = 0; i < PrefixLength; i++)
            chars[i] = PrefixAlphabet[RandomNumberGenerator.GetInt32(PrefixAlphabet.Length)];
        return new string(chars);
    }

    private static string GenerateSecret()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(SecretByteLength);
        // base64url per the spec: '+'/'/' are awkward in a header value and in a URL if one is
        // ever built from the key; padding '=' carries no information here.
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
