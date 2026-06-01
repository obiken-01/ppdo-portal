using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace PPDO.Functions.Functions;

/// <summary>
/// Handles HTTP OPTIONS preflight requests for all API routes.
///
/// Problem: the Azure Functions host intercepts OPTIONS requests at the host layer
/// and returns 404 before they ever reach the isolated worker process — so
/// Program.cs middleware never fires for preflight.
///
/// Solution: register a catch-all OPTIONS function so the host routes preflight
/// to the worker. Once the invocation reaches the worker, the CORS middleware in
/// Program.cs adds the Access-Control-Allow-* headers and this handler returns
/// 204 No Content.
///
/// The wildcard route "{*path}" matches every URL under /api/.
/// </summary>
public sealed class CorsFunctions
{
    [Function("CorsPreflightHandler")]
    public HttpResponseData HandlePreflight(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "{*path}")]
        HttpRequestData req)
    {
        // The CORS headers are added by the middleware in Program.cs.
        // This handler only needs to return the correct status code.
        return req.CreateResponse(HttpStatusCode.NoContent);
    }
}
