namespace PPDO.Application.Common;

/// <summary>
/// String constants for <c>AipActivity.EsreCode</c> — free text on the entity (Excel import
/// doesn't validate it), but manual entry (RAL-62) presents it as a select of these 4 values.
/// </summary>
public static class AipEsreCode
{
    public const string SocialServices    = "SS";
    public const string EconomicServices  = "ES";
    public const string InfrastructureDev = "ID";
    public const string Environment       = "EN";

    public static readonly string[] AllowedValues = [SocialServices, EconomicServices, InfrastructureDev, Environment];
}
