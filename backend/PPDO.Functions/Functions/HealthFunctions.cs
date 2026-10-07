using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Services;

namespace PPDO.Functions.Functions;

/// <summary>
/// GET /api/health — lightweight liveness probe. Public: no JWT (login warm-up, the deploy smoke
/// test and external probes all call it without one).
///
/// Purpose:
///   1. Wakes up the Azure Functions instance (Consumption plan scales to zero
///      after ~10 min of no traffic).
///   2. Confirms the database is reachable (Azure SQL Basic tier — always provisioned, no
///      auto-pause since 2026-08-12) with a bare SELECT 1.
///   3. Returns a status payload so the login page can show a live indicator.
///
/// ⚠️ **The body never carries exception text.** The endpoint is anonymous, and a SqlClient message can
/// include the server, database or login name. The failure is logged by <see cref="IHealthService"/>
/// (Application Insights); the response says only <c>database: "unavailable"</c> (PPDO-142). This handler
/// holds no <c>AppDbContext</c> and no logging — it asks the service and shapes the response.
/// </summary>
public sealed class HealthFunctions
{
    private readonly IHealthService _health;

    public HealthFunctions(IHealthService health)
    {
        _health = health;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        bool dbOk = await _health.CheckDatabaseAsync(cancellationToken);

        var payload = new
        {
            status   = dbOk ? "ok" : "degraded",
            api      = "ok",
            database = dbOk ? "ok" : "unavailable",
            utc      = DateTime.UtcNow,
        };

        HttpStatusCode statusCode = dbOk ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
        HttpResponseData response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }),
            cancellationToken);

        return response;
    }
}
