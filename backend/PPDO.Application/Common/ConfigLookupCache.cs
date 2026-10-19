using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// Shared, cached whole-table reads of the divisions / offices / funding_sources config tables
/// (PPDO-44). Config tables ONLY — never cache anything keyed by user, office or division scope;
/// callers apply their own scope filters to the returned list.
/// Returned lists are shared: callers must treat them (and the entities in them) as read-only.
/// Code that mutates these entities must load its own tracked copy from the repository.
/// </summary>
public interface IConfigLookupCache
{
    Task<IReadOnlyList<Division>>      GetDivisionsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<Office>>        GetOfficesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<FundingSource>> GetFundingSourcesAsync(CancellationToken ct = default);

    /// <summary>Call AFTER a successful save of any division write.</summary>
    void InvalidateDivisions();
    /// <summary>Call AFTER a successful save of any office write.</summary>
    void InvalidateOffices();
    /// <summary>Call AFTER a successful save of any funding-source write.</summary>
    void InvalidateFundingSources();
}

/// <summary>
/// Singleton. Repositories are scoped, so each cache miss opens a fresh scope via
/// <see cref="IServiceScopeFactory"/> — a DbContext is never captured by the singleton.
/// </summary>
public sealed class ConfigLookupCache : IConfigLookupCache
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(10);

    private const string DivisionsKey      = "config:divisions";
    private const string OfficesKey        = "config:offices";
    private const string FundingSourcesKey = "config:funding_sources";

    private readonly IMemoryCache _cache;
    private readonly IServiceScopeFactory _scopes;
    private readonly TimeSpan _ttl;

    public ConfigLookupCache(IMemoryCache cache, IServiceScopeFactory scopes)
        : this(cache, scopes, DefaultTtl) { }

    public ConfigLookupCache(IMemoryCache cache, IServiceScopeFactory scopes, TimeSpan ttl)
    {
        _cache  = cache;
        _scopes = scopes;
        _ttl    = ttl;
    }

    public Task<IReadOnlyList<Division>> GetDivisionsAsync(CancellationToken ct = default)
        => GetOrLoadAsync<Division>(DivisionsKey, ct);

    public Task<IReadOnlyList<Office>> GetOfficesAsync(CancellationToken ct = default)
        => GetOrLoadAsync<Office>(OfficesKey, ct);

    public Task<IReadOnlyList<FundingSource>> GetFundingSourcesAsync(CancellationToken ct = default)
        => GetOrLoadAsync<FundingSource>(FundingSourcesKey, ct);

    public void InvalidateDivisions()      => _cache.Remove(DivisionsKey);
    public void InvalidateOffices()        => _cache.Remove(OfficesKey);
    public void InvalidateFundingSources() => _cache.Remove(FundingSourcesKey);

    private async Task<IReadOnlyList<T>> GetOrLoadAsync<T>(string key, CancellationToken ct) where T : class
    {
        if (_cache.TryGetValue(key, out IReadOnlyList<T>? hit) && hit is not null)
            return hit;

        using IServiceScope scope = _scopes.CreateScope();
        IRepository<T> repo = scope.ServiceProvider.GetRequiredService<IRepository<T>>();
        IReadOnlyList<T> rows = await repo.GetAllAsync(ct);

        _cache.Set(key, rows, _ttl);
        return rows;
    }
}
