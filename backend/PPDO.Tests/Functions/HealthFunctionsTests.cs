using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;
using PPDO.Application.Services;
using PPDO.Functions.Functions;

namespace PPDO.Tests.Functions;

/// <summary>
/// PPDO-142 — GET /api/health stays public and keeps its shape; the one change is that a failed
/// database check no longer carries <c>error</c> (raw exception text, readable by anyone).
/// Consumers: the login-page indicator (<c>database === "ok"</c>), the deploy smoke test (HTTP code),
/// and the warm-up call — none reads <c>error</c>.
/// </summary>
public sealed class HealthFunctionsTests
{
    private static async Task<(HttpResponseData Response, JsonElement Body)> Call(bool databaseUp)
    {
        Mock<IHealthService> health = new();
        health.Setup(h => h.CheckDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(databaseUp);

        HttpResponseData response = await new HealthFunctions(health.Object).Health(
            FunctionHttp.Get("", authorizationHeader: null, path: "health"), CancellationToken.None);

        using JsonDocument doc = JsonDocument.Parse(FunctionHttp.BodyText(response));
        return (response, doc.RootElement.Clone());
    }

    [Fact]
    public async Task Health_WhenTheDatabaseIsUp_Returns200WithTheUnchangedShape()
    {
        (HttpResponseData response, JsonElement body) = await Call(databaseUp: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("ok", body.GetProperty("api").GetString());
        Assert.Equal("ok", body.GetProperty("database").GetString());
        Assert.Equal(["status", "api", "database", "utc"],
            body.EnumerateObject().Select(p => p.Name).ToArray());   // names, camelCase, and their order
    }

    [Fact]
    public async Task Health_WhenTheDatabaseIsDown_Returns503WithNoErrorKeyAtAll()
    {
        (HttpResponseData response, JsonElement body) = await Call(databaseUp: false);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("degraded", body.GetProperty("status").GetString());
        Assert.Equal("ok", body.GetProperty("api").GetString());
        Assert.Equal("unavailable", body.GetProperty("database").GetString());
        Assert.False(body.TryGetProperty("error", out _), "the 503 body must not carry an error key");
        Assert.Equal(["status", "api", "database", "utc"],
            body.EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    public async Task Health_KeepsTheJsonContentType()
    {
        (HttpResponseData response, _) = await Call(databaseUp: true);

        Assert.Equal("application/json; charset=utf-8", response.Headers.GetValues("Content-Type").Single());
    }

    [Fact]
    public async Task Health_StaysPublic_ItNeverAsksForAUser()
    {
        // Login warm-up, the deploy smoke test and external probes call it without a token.
        Mock<IHealthService> health = new();
        health.Setup(h => h.CheckDatabaseAsync(It.IsAny<CancellationToken>())).ReturnsAsync(true);

        HttpResponseData response = await new HealthFunctions(health.Object).Health(
            FunctionHttp.Get("", authorizationHeader: null, path: "health"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);   // no Authorization header, still 200
    }
}
