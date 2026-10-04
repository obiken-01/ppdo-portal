using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using PPDO.Functions.Middleware;

namespace PPDO.Tests.Functions;

/// <summary>
/// Tests for API response compression (PPDO-182).
///
/// <para>The pipeline tests build the real ASP.NET pipeline the filter produces — the real
/// <c>ResponseCompressionMiddleware</c>, the real services from
/// <see cref="ResponseCompressionStartupFilter.AddApiResponseCompression"/> — with a terminal
/// stage standing in for the functions' HTTP proxying. What they cannot prove is that the Functions
/// host passes <c>Content-Encoding</c> through; that was checked against <c>func start</c> and is on
/// the PR.</para>
/// </summary>
public class ResponseCompressionStartupFilterTests
{
    /// <summary>Large enough that any compressor wins: the shape of an AIP detail response.</summary>
    private static readonly string LargeJson =
        "{\"data\":[" + string.Join(",", Enumerable.Range(0, 500).Select(i =>
            $"{{\"id\":{i},\"name\":\"Activity {i}\",\"refCode\":\"1000-000-1-01-{i:000}\"}}")) + "]}";

    private static async Task<HttpContext> SendAsync(string path, string? acceptEncoding)
    {
        ServiceCollection services = new();
        services.AddLogging();
        ResponseCompressionStartupFilter.AddApiResponseCompression(services);
        IServiceProvider provider = services.BuildServiceProvider();

        ApplicationBuilder app = new(provider);
        IStartupFilter filter = provider.GetServices<IStartupFilter>()
            .OfType<ResponseCompressionStartupFilter>()
            .Single();
        // The terminal stage plays the functions' HTTP proxying: it writes a JSON body.
        filter.Configure(builder => builder.Run(async ctx =>
        {
            ctx.Response.ContentType = "application/json; charset=utf-8";
            await ctx.Response.WriteAsync(LargeJson);
        }))(app);
        RequestDelegate pipeline = app.Build();

        DefaultHttpContext http = new() { RequestServices = provider };
        http.Request.Path = path;
        if (acceptEncoding is not null) http.Request.Headers.AcceptEncoding = acceptEncoding;
        http.Response.Body = new MemoryStream();
        await pipeline(http);
        http.Response.Body.Position = 0;
        return http;
    }

    // ── IsExcluded ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    [InlineData("/api/auth/me")]
    [InlineData("/API/Auth/Login")]
    public void IsExcluded_AuthPath_ReturnsTrue(string path)
        => Assert.True(ResponseCompressionStartupFilter.IsExcluded(path));

    [Theory]
    [InlineData("/api/budget-planning/aip/13")]
    [InlineData("/api/config/price-index/picker")]
    [InlineData("/api/authors")] // segment match, not a prefix match on the string
    [InlineData("/api/users/auth")]
    public void IsExcluded_OtherPath_ReturnsFalse(string path)
        => Assert.False(ResponseCompressionStartupFilter.IsExcluded(path));

    // ── Pipeline ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Configure_GzipAccepted_CompressesJsonAndRoundTrips()
    {
        HttpContext http = await SendAsync("/api/budget-planning/aip/13", "gzip");

        Assert.Equal("gzip", http.Response.Headers.ContentEncoding.ToString());
        Assert.Contains("Accept-Encoding", http.Response.Headers.Vary.ToString());
        Assert.True(http.Response.Body.Length < Encoding.UTF8.GetByteCount(LargeJson) / 2);

        await using GZipStream gzip = new(http.Response.Body, CompressionMode.Decompress);
        Assert.Equal(LargeJson, await new StreamReader(gzip).ReadToEndAsync());
    }

    [Fact]
    public async Task Configure_BrotliAccepted_PrefersBrotli()
    {
        HttpContext http = await SendAsync("/api/budget-planning/aip/13", "gzip, deflate, br");

        Assert.Equal("br", http.Response.Headers.ContentEncoding.ToString());
    }

    [Fact]
    public async Task Configure_NoAcceptEncoding_SendsPlainJson()
    {
        HttpContext http = await SendAsync("/api/budget-planning/aip/13", acceptEncoding: null);

        Assert.True(string.IsNullOrEmpty(http.Response.Headers.ContentEncoding.ToString()));
        Assert.Equal(LargeJson, await new StreamReader(http.Response.Body).ReadToEndAsync());
    }

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/api/auth/refresh")]
    public async Task Configure_AuthPath_NeverCompressed(string path)
    {
        // BREACH: these responses carry the access token. Red-tested by removing the UseWhen.
        HttpContext http = await SendAsync(path, "gzip, br");

        Assert.True(string.IsNullOrEmpty(http.Response.Headers.ContentEncoding.ToString()));
        Assert.Equal(LargeJson, await new StreamReader(http.Response.Body).ReadToEndAsync());
    }
}
