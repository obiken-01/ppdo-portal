using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace PPDO.Functions.Middleware;

/// <summary>
/// Gzip/Brotli for API responses (PPDO-182).
///
/// <para>Before this, every response left the worker uncompressed. The FY 2027 AIP detail is
/// 1.5 MB of JSON, 155 KB gzipped, and production sent all 1.5 MB (Performance_Audit_2026-10-04,
/// O1).</para>
///
/// <para><b>Why a startup filter and not a worker middleware.</b> <c>ConfigureFunctionsWebApplication</c>
/// runs the worker on ASP.NET Core but never hands out the <c>IApplicationBuilder</c>, so
/// <c>app.UseResponseCompression()</c> has nowhere to go. An <see cref="IStartupFilter"/> registered in
/// DI is applied by the web host to the front of that pipeline, so ASP.NET's own compression
/// middleware runs around the functions' HTTP proxying. That buys its handling of
/// <c>Accept-Encoding</c>, <c>Vary</c>, MIME types and minimum sizes rather than a hand-rolled copy.</para>
///
/// <para><b>Auth responses are never compressed.</b> HTTPS compression is off by default in ASP.NET
/// because of BREACH: compressing a secret alongside attacker-influenced text in the same response
/// lets response sizes leak the secret. The login and refresh responses carry the access token, so
/// <see cref="IsExcluded"/> keeps <c>/api/auth/*</c> out entirely. Everything else carries no token.</para>
/// </summary>
internal sealed class ResponseCompressionStartupFilter : IStartupFilter
{
    /// <summary>Paths that are never compressed. See the BREACH note above.</summary>
    internal static bool IsExcluded(PathString path)
        => path.StartsWithSegments("/api/auth", StringComparison.OrdinalIgnoreCase);

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseWhen(ctx => !IsExcluded(ctx.Request.Path), branch => branch.UseResponseCompression());
            next(app);
        };

    /// <summary>Registers the compression services and this filter. Called from <c>Program.cs</c>.</summary>
    internal static IServiceCollection AddApiResponseCompression(IServiceCollection services)
    {
        services.AddResponseCompression(options =>
        {
            // Required for HTTPS (production and UAT). Safe here because /api/auth is excluded.
            options.EnableForHttps = true;
            options.Providers.Add<BrotliCompressionProvider>();
            options.Providers.Add<GzipCompressionProvider>();
            // The defaults already include application/json; listed so the intent is on the page.
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/json"]);
        });
        // Fastest, not Optimal: the Consumption plan bills CPU, and Fastest already gets most of
        // the ratio on repetitive JSON.
        services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
        services.AddTransient<IStartupFilter, ResponseCompressionStartupFilter>();
        return services;
    }
}
