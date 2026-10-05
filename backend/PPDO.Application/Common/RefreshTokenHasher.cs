namespace PPDO.Application.Common;

/// <summary>
/// How a refresh token is stored: <c>SHA-256(token)</c>, lower-case hex, 64 characters (PPDO-141).
///
/// The cookie carries the raw token; <c>Users.RefreshToken</c> holds only this hash, so a person who can
/// read the table cannot lift a live session out of it. A plain SHA-256 (not BCrypt) is right here: the
/// token is 64 random bytes — nothing to brute-force — and the column must support an equality lookup,
/// which a salted hash would not.
///
/// ⚠️ Deliberately the same shape as <see cref="ApiKeyGenerator.Hash"/> rather than a second format, and
/// it delegates to it so the two cannot drift apart. 64 hex characters fit the existing
/// <c>nvarchar(100)</c> column, so there is no migration.
///
/// Never log a token or its hash.
/// </summary>
public static class RefreshTokenHasher
{
    /// <summary>The value to store in, or look up against, <c>Users.RefreshToken</c>.</summary>
    public static string Hash(string rawToken) => ApiKeyGenerator.Hash(rawToken);
}
