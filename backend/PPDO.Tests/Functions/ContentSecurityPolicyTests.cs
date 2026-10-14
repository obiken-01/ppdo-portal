using System.Text.Json;
using System.Text.RegularExpressions;

namespace PPDO.Tests.Functions;

/// <summary>
/// Pins PPDO-199's Content-Security-Policy (<c>frontend/staticwebapp.config.json</c>) to the
/// deploy workflows.
///
/// ⚠️ The API host is baked into the frontend build (<c>NEXT_PUBLIC_API_BASE_URL</c> in
/// <c>.github/workflows/deploy*.yml</c>), and the CSP's <c>connect-src</c> lists the hosts the
/// page may call. Change the host in a workflow without the CSP — the MIS move would — and every
/// API call fails in the browser while the build and the API itself look fine. This test makes
/// that a build failure instead.
/// </summary>
public sealed class ContentSecurityPolicyTests
{
    [Fact]
    public void ConnectSrc_AllowsEveryApiHostADeployWorkflowBakesIn()
    {
        string connectSrc = Directive("connect-src");
        string[] hosts = DeployedApiOrigins();

        Assert.NotEmpty(hosts);
        foreach (string origin in hosts)
            Assert.True(connectSrc.Split(' ').Contains(origin),
                $"{origin} is the API a deploy workflow builds against, but the CSP's connect-src " +
                $"('{connectSrc}') does not allow it. Add it to frontend/staticwebapp.config.json.");
    }

    [Theory]
    [InlineData("frame-ancestors", "'none'")]
    [InlineData("object-src", "'none'")]
    [InlineData("base-uri", "'self'")]
    [InlineData("form-action", "'self'")]
    [InlineData("default-src", "'self'")]
    public void LockedDownDirectives_StayLockedDown(string directive, string expected)
        => Assert.Equal(expected, Directive(directive));

    [Fact]
    public void Hsts_IsSent()
        => Assert.StartsWith("max-age=", GlobalHeaders().GetProperty("Strict-Transport-Security").GetString());

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string Directive(string name)
    {
        string policy = GlobalHeaders().GetProperty("Content-Security-Policy").GetString()!;
        string? match = policy.Split(';')
            .Select(d => d.Trim())
            .FirstOrDefault(d => d.StartsWith(name + " ", StringComparison.Ordinal));

        Assert.True(match is not null, $"The CSP has no {name} directive.");
        return match![(name.Length + 1)..].Trim();
    }

    private static JsonElement GlobalHeaders()
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(RepoPath(Path.Combine("frontend", "staticwebapp.config.json"))));
        return doc.RootElement.GetProperty("globalHeaders").Clone();
    }

    /// <summary>The origin of every <c>NEXT_PUBLIC_API_BASE_URL</c> in a deploy workflow.</summary>
    private static string[] DeployedApiOrigins()
    {
        string workflows = RepoPath(Path.Combine(".github", "workflows"));
        Regex baseUrl = new(@"NEXT_PUBLIC_API_BASE_URL:\s*(https://[^/\s]+)");

        return Directory.EnumerateFiles(workflows, "deploy*.yml")
            .SelectMany(f => baseUrl.Matches(File.ReadAllText(f)).Select(m => m.Groups[1].Value))
            .Distinct()
            .ToArray();
    }

    private static string RepoPath(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Path.Exists(Path.Combine(dir.FullName, relative)))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, relative);
    }
}
