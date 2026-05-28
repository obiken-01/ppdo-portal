namespace PPDO.Application.Settings;

/// <summary>
/// Bound from the "Jwt" configuration section via IOptions&lt;JwtSettings&gt;.
/// In local.settings.json, keys use double-underscore: Jwt__SecretKey → Jwt:SecretKey.
/// </summary>
public sealed class JwtSettings
{
    public string SecretKey { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public string Audience { get; init; } = string.Empty;
    public int AccessTokenExpiryMinutes { get; init; } = 15;
    public int RefreshTokenExpiryDays { get; init; } = 7;
}
