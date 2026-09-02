namespace PPDO.Application.DTOs.Config
{
    /// <summary>Read model for an eSRE classification code (RAL-248).</summary>
    public sealed record EsreCodeDto(
        int Id,
        string Code,
        string Name,
        string? Description,
        bool IsActive);

    /// <summary>
    /// Create/update body. <c>Code</c> is the unique key and is normalised to upper case by the
    /// service, so "ss" and "SS" are the same row rather than two.
    /// </summary>
    public sealed record UpsertEsreCodeDto(
        string Code,
        string Name,
        string? Description = null,
        bool IsActive = true);
}