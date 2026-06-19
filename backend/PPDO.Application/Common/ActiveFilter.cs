namespace PPDO.Application.Common;

/// <summary>
/// Config list filter for the soft-delete flag, mapping the <c>?active</c> query param:
///   true  → <see cref="Active"/>   (exclude deactivated — used by dropdown variants)
///   false → <see cref="Inactive"/> (only deactivated)
///   all   → <see cref="All"/>      (everything)
/// </summary>
public enum ActiveFilter
{
    Active,
    Inactive,
    All,
}

public static class ActiveFilterParser
{
    /// <summary>Parses the <c>?active</c> query value. Defaults to <see cref="ActiveFilter.All"/>.</summary>
    public static ActiveFilter Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "true"  => ActiveFilter.Active,
        "false" => ActiveFilter.Inactive,
        _        => ActiveFilter.All,
    };
}
