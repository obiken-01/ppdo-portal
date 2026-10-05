namespace PPDO.Domain.Interfaces;

/// <summary>
/// Read-only label lookups behind the Investment Planning dashboard's Recent activity band
/// (PPDO-181 / B4). An audit row says "update on budget_ceilings #13"; turning that into
/// "updated PTO's FY 2028 ceiling" needs the office code and year of ceiling 13. Every method takes
/// the whole set of ids the page of entries touches and answers with ONE query — never one per
/// row (<c>docs/PERFORMANCE_GUIDELINES.md</c>). An id with no row (the record was deleted since) is
/// simply absent from the result; the caller falls back to a generic sentence.
///
/// All reads are untracked: nothing here is ever edited.
/// </summary>
public interface IActivityLabelRepository
{
    /// <summary>budget_ceilings by id → the office it belongs to and its fiscal year.</summary>
    Task<IReadOnlyDictionary<int, CeilingLabel>> GetCeilingLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>aip_offices (sector group rows) by id → the config office and the record's fiscal year.</summary>
    Task<IReadOnlyDictionary<int, AipOfficeLabel>> GetAipOfficeLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>aip_programs by id → name and the office it sits under.</summary>
    Task<IReadOnlyDictionary<int, AipProgramLabel>> GetAipProgramLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>aip_activities by id → the project they belong to and that project's office.</summary>
    Task<IReadOnlyDictionary<int, AipActivityLabel>> GetAipActivityLabelsAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>offices by id → office code.</summary>
    Task<IReadOnlyDictionary<int, string>> GetOfficeCodesAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>divisions by id → name.</summary>
    Task<IReadOnlyDictionary<int, string>> GetDivisionNamesAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);

    /// <summary>funding sources by id → name.</summary>
    Task<IReadOnlyDictionary<int, string>> GetFundingSourceNamesAsync(
        IReadOnlyCollection<int> ids, CancellationToken cancellationToken = default);
}

/// <summary>A budget ceiling's office code (null when the office is gone) and fiscal year.</summary>
public sealed record CeilingLabel(string? OfficeCode, int FiscalYear);

/// <summary>An AIP sector group's config office code (null for an unlinked group) and fiscal year.</summary>
public sealed record AipOfficeLabel(string? OfficeCode, int FiscalYear);

/// <summary>An AIP program's name and its office code.</summary>
public sealed record AipProgramLabel(string? OfficeCode, string Name);

/// <summary>An AIP activity's project name and office code.</summary>
public sealed record AipActivityLabel(string? OfficeCode, string ProjectName);
