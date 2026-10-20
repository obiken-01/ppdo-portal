using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>PPDO-44: shared cache for the divisions / offices / funding_sources config tables.</summary>
public class ConfigLookupCacheTests
{
    private sealed class Harness
    {
        public Mock<IRepository<Division>>      Divisions { get; } = new();
        public Mock<IRepository<Office>>        Offices   { get; } = new();
        public Mock<IRepository<FundingSource>> Funds     { get; } = new();
        public int ScopesCreated;
        public ConfigLookupCache Sut { get; }

        public Harness(TimeSpan? ttl = null)
        {
            Divisions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new List<Division> { new() { Id = 1, Name = "Planning" } });
            Offices.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<Office> { new() { Id = 1, OfficeName = "PPDO" } });
            Funds.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new List<FundingSource> { new() { Id = 1, Code = "GF" } });

            ServiceCollection services = new();
            services.AddScoped(_ => { ScopesCreated++; return Divisions.Object; });
            services.AddScoped(_ => Offices.Object);
            services.AddScoped(_ => Funds.Object);
            ServiceProvider sp = services.BuildServiceProvider();

            Sut = ttl is null
                ? new ConfigLookupCache(new MemoryCache(new MemoryCacheOptions()), sp.GetRequiredService<IServiceScopeFactory>())
                : new ConfigLookupCache(new MemoryCache(new MemoryCacheOptions()), sp.GetRequiredService<IServiceScopeFactory>(), ttl.Value);
        }
    }

    [Fact]
    public async Task GetDivisionsAsync_SecondCall_ServedFromCacheWithSameData()
    {
        Harness h = new();
        IReadOnlyList<Division> first  = await h.Sut.GetDivisionsAsync();
        IReadOnlyList<Division> second = await h.Sut.GetDivisionsAsync();

        Assert.Equal(first.Select(d => d.Name), second.Select(d => d.Name));
        h.Divisions.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetOfficesAsync_SecondCall_ServedFromCache()
    {
        Harness h = new();
        await h.Sut.GetOfficesAsync();
        await h.Sut.GetOfficesAsync();
        h.Offices.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetFundingSourcesAsync_SecondCall_ServedFromCache()
    {
        Harness h = new();
        await h.Sut.GetFundingSourcesAsync();
        await h.Sut.GetFundingSourcesAsync();
        h.Funds.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateDivisions_NextReadHitsDatabaseAndSeesNewValue()
    {
        Harness h = new();
        await h.Sut.GetDivisionsAsync();
        h.Divisions.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<Division> { new() { Id = 1, Name = "Renamed" } });

        h.Sut.InvalidateDivisions();
        IReadOnlyList<Division> after = await h.Sut.GetDivisionsAsync();

        Assert.Equal("Renamed", after.Single().Name);
    }

    [Fact]
    public async Task InvalidateDivisions_DoesNotEvictOfficesOrFundingSources()
    {
        Harness h = new();
        await h.Sut.GetOfficesAsync();
        await h.Sut.GetFundingSourcesAsync();

        h.Sut.InvalidateDivisions();
        await h.Sut.GetOfficesAsync();
        await h.Sut.GetFundingSourcesAsync();

        h.Offices.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        h.Funds.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidateOffices_NextReadHitsDatabase()
    {
        Harness h = new();
        await h.Sut.GetOfficesAsync();
        h.Sut.InvalidateOffices();
        await h.Sut.GetOfficesAsync();
        h.Offices.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task InvalidateFundingSources_NextReadHitsDatabase()
    {
        Harness h = new();
        await h.Sut.GetFundingSourcesAsync();
        h.Sut.InvalidateFundingSources();
        await h.Sut.GetFundingSourcesAsync();
        h.Funds.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetDivisionsAsync_AfterTtlExpiry_ReadsAgain()
    {
        Harness h = new(TimeSpan.FromMilliseconds(100));
        await h.Sut.GetDivisionsAsync();
        await Task.Delay(400);
        await h.Sut.GetDivisionsAsync();
        h.Divisions.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetDivisionsAsync_ReadsInFreshScope_NotCallerContext()
    {
        Harness h = new();
        await h.Sut.GetDivisionsAsync();
        Assert.Equal(1, h.ScopesCreated);
    }
}
