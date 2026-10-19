using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Test double for <see cref="IConfigLookupCache"/> that reads straight through to the supplied
/// repositories on every call (no caching), so service tests keep asserting against their repo
/// mocks. Caching behaviour itself is covered by <c>ConfigLookupCacheTests</c>.
/// </summary>
internal sealed class PassThroughConfigCache : IConfigLookupCache
{
    private readonly IRepository<Division>?      _divisions;
    private readonly IRepository<Office>?        _offices;
    private readonly IRepository<FundingSource>? _funds;

    public PassThroughConfigCache(
        IRepository<Division>? divisions = null,
        IRepository<Office>? offices = null,
        IRepository<FundingSource>? funds = null)
    {
        _divisions = divisions;
        _offices   = offices;
        _funds     = funds;
    }

    public async Task<IReadOnlyList<Division>> GetDivisionsAsync(CancellationToken ct = default)
        => _divisions is null ? [] : await _divisions.GetAllAsync(ct);

    public async Task<IReadOnlyList<Office>> GetOfficesAsync(CancellationToken ct = default)
        => _offices is null ? [] : await _offices.GetAllAsync(ct);

    public async Task<IReadOnlyList<FundingSource>> GetFundingSourcesAsync(CancellationToken ct = default)
        => _funds is null ? [] : await _funds.GetAllAsync(ct);

    public void InvalidateDivisions() { }
    public void InvalidateOffices() { }
    public void InvalidateFundingSources() { }
}
