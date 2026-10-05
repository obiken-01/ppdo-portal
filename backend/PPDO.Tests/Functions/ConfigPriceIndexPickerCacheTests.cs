using System.Net;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// The price-index picker's conditional GET (PPDO-183): the browser keeps the ~6,400-row
/// catalogue and revalidates it with <c>If-None-Match</c> on every visit.
///
/// <para>The rules that matter: an unchanged catalogue answers 304 <b>without loading the rows</b>
/// (the strict mock fails if <c>GetPickerListAsync</c> is called), a changed one answers 200 with
/// the new ETag, and the JWT check still runs first, so a 304 never answers an anonymous caller.</para>
/// </summary>
public sealed class ConfigPriceIndexPickerCacheTests
{
    private const string ETag = "W/\"pi1-6398-638640000000000000\"";

    private readonly Mock<IPriceIndexService> _priceIndex  = new(MockBehavior.Strict);
    private readonly Mock<IJwtValidator>      _jwt         = new(MockBehavior.Strict);
    private readonly Mock<IPermissionService> _permissions = new(MockBehavior.Loose);

    private ConfigPriceIndexFunctions Sut => new(_priceIndex.Object, _jwt.Object, _permissions.Object);

    private static readonly User Caller = new()
    {
        Id = Guid.NewGuid(), FullName = "Encoder", Username = "encoder", PasswordHash = "hash",
        Role = UserRole.Staff,
    };

    public ConfigPriceIndexPickerCacheTests()
    {
        _jwt.Setup(j => j.ValidateAsync("Bearer test-token", It.IsAny<CancellationToken>())).ReturnsAsync(Caller);
        _jwt.Setup(j => j.ValidateAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);
        _priceIndex.Setup(p => p.GetPickerETagAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ETag);
    }

    private static FakeHttpRequestData Request(string? ifNoneMatch, string? auth = "Bearer test-token")
    {
        FakeHttpRequestData req = FunctionHttp.Get("active=true", auth, "config/price-index/picker");
        if (ifNoneMatch is not null) req.Headers.Add("If-None-Match", ifNoneMatch);
        return req;
    }

    private void SetupList() => _priceIndex
        .Setup(p => p.GetPickerListAsync(It.IsAny<string?>(), ActiveFilter.Active, It.IsAny<CancellationToken>()))
        .ReturnsAsync([new PriceIndexPickerItemDto(1, "Bond paper", "ream", 250m, false, null)]);

    /// <summary>Both directives, in any order: the header collection normalises the order.</summary>
    private static void AssertRevalidatePolicy(HttpResponseData res)
    {
        string[] directives = (FunctionHttp.Header(res, "Cache-Control") ?? "")
            .Split(',', StringSplitOptions.TrimEntries);
        Assert.Contains("private", directives);
        Assert.Contains("no-cache", directives);
    }

    // ── PickerList ────────────────────────────────────────────────────────────

    [Fact]
    public async Task PickerList_NoIfNoneMatch_Returns200WithETagAndRevalidatePolicy()
    {
        SetupList();

        HttpResponseData res = await Sut.PickerList(Request(ifNoneMatch: null), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(ETag, FunctionHttp.Header(res, "ETag"));
        AssertRevalidatePolicy(res);
        Assert.Contains("Bond paper", FunctionHttp.BodyText(res));
    }

    [Fact]
    public async Task PickerList_MatchingETag_Returns304WithoutLoadingRows()
    {
        // No SetupList: the strict mock throws if the catalogue is read.
        HttpResponseData res = await Sut.PickerList(Request(ETag), CancellationToken.None);

        Assert.Equal(HttpStatusCode.NotModified, res.StatusCode);
        Assert.Equal(ETag, FunctionHttp.Header(res, "ETag"));
        AssertRevalidatePolicy(res);
        Assert.Equal(string.Empty, FunctionHttp.BodyText(res));
        _priceIndex.Verify(p => p.GetPickerListAsync(
            It.IsAny<string?>(), It.IsAny<ActiveFilter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PickerList_StaleETag_Returns200WithCurrentETag()
    {
        SetupList();

        HttpResponseData res = await Sut.PickerList(Request("W/\"pi1-6397-1\""), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(ETag, FunctionHttp.Header(res, "ETag"));
    }

    [Fact]
    public async Task PickerList_MatchingETagButNoToken_Returns401Not304()
    {
        HttpResponseData res = await Sut.PickerList(Request(ETag, auth: null), CancellationToken.None);

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        _priceIndex.Verify(p => p.GetPickerETagAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── IfNoneMatchMatches ────────────────────────────────────────────────────

    [Theory]
    [InlineData("W/\"pi1-6398-638640000000000000\"")]          // exact
    [InlineData("\"pi1-6398-638640000000000000\"")]            // strong form of the same tag: weak comparison
    [InlineData("\"other\", W/\"pi1-6398-638640000000000000\"")] // in a list
    [InlineData("*")]
    public void IfNoneMatchMatches_Matching_ReturnsTrue(string header)
        => Assert.True(ConfigHttp.IfNoneMatchMatches(Request(header), ETag));

    [Theory]
    [InlineData("W/\"pi1-6397-638640000000000000\"")] // one row fewer
    [InlineData("W/\"pi2-6398-638640000000000000\"")] // shape version bumped
    [InlineData("")]
    public void IfNoneMatchMatches_Different_ReturnsFalse(string header)
        => Assert.False(ConfigHttp.IfNoneMatchMatches(Request(header), ETag));

    [Fact]
    public void IfNoneMatchMatches_NoHeader_ReturnsFalse()
        => Assert.False(ConfigHttp.IfNoneMatchMatches(Request(ifNoneMatch: null), ETag));
}
