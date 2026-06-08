using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using PPDO.Infrastructure.Data;

namespace PPDO.Functions.Functions;

/// <summary>
/// GET /api/health — lightweight liveness probe.
///
/// Purpose:
///   1. Wakes up the Azure Functions instance (Consumption plan scales to zero
///      after ~10 min of no traffic).
///   2. Wakes up Azure SQL (Free tier auto-pauses after 1 hour of inactivity).
///   3. Returns a status payload so the login page can show a live indicator.
///
/// The DB check is intentionally a bare SELECT 1 — no joins, no auth, minimal load.
/// </summary>
public sealed class HealthFunctions
{
    private readonly AppDbContext _db;

    public HealthFunctions(AppDbContext db)
    {
        _db = db;
    }

    [Function("Health")]
    public async Task<HttpResponseData> Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")]
        HttpRequestData req,
        CancellationToken cancellationToken)
    {
        bool dbOk = false;
        string? dbError = null;

        try
        {
            // SELECT 1 — wakes up Azure SQL and confirms connectivity.
            dbOk = await _db.Database
                .ExecuteSqlRawAsync("SELECT 1", cancellationToken)
                .ContinueWith(t => !t.IsFaulted, cancellationToken);
        }
        catch (Exception ex)
        {
            dbError = ex.Message;
        }

        var payload = new
        {
            status   = dbOk ? "ok" : "degraded",
            api      = "ok",
            database = dbOk ? "ok" : "unavailable",
            error    = dbError,
            utc      = DateTime.UtcNow,
        };

        HttpStatusCode statusCode = dbOk ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
        HttpResponseData response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(
            JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition      = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            }),
            cancellationToken);

        return response;
    }
}
