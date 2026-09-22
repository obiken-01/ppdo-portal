using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;

namespace PPDO.Functions.Middleware;

/// <summary>
/// Catches anything a handler lets escape, logs it with business context, and returns the
/// standard <c>{ data, error, message }</c> envelope with a 500 (PPDO-43).
///
/// <para>Before this existed the worker pipeline had exactly one stage in it — CORS — so an
/// unhandled failure (SQL timeout, null deref in a mapper, EF concurrency conflict) propagated
/// to the Functions host, which returned a 500 in whatever shape it chose, with no
/// <c>LogError</c> and no indication of who was doing what. 240 HTTP endpoints, and five
/// <c>catch (Exception</c> blocks in the whole solution.</para>
///
/// <para><b>Registration order matters and is not arbitrary.</b> <c>worker.Use</c> runs stages
/// outermost-first in registration order, so this one is registered <i>before</i> the CORS block
/// and therefore wraps it. That is what keeps CORS headers on a 500: the CORS stage sets them on
/// the way in, before its own <c>await next</c>, so they are already on the response when an
/// exception unwinds back out through it. Register this one second and a failed request returns
/// without CORS headers — the browser then reports an opaque network error instead of a 500,
/// which is a genuinely nasty thing to debug from the far end.</para>
/// </summary>
internal sealed class ExceptionHandlingMiddleware : IFunctionsWorkerMiddleware
{
    /// <summary>
    /// What the caller is told. Deliberately says nothing about the failure — no exception type,
    /// no message, no stack trace. The invocation id is included on purpose: it is not sensitive,
    /// it correlates to Application Insights' <c>operation_Id</c>, and it turns "the site broke"
    /// into a support request that can actually be looked up.
    /// </summary>
    internal const string ClientMessage =
        "An unexpected error occurred. Please try again. If it keeps happening, quote reference {0}.";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(ILogger<ExceptionHandlingMiddleware> logger) => _logger = logger;

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        // CallerContext is scoped per invocation and populated by the JWT check inside the
        // handler. Resolved here (not read here) so the scope can read it later — see
        // InvocationLogScope for why that distinction is the whole point.
        CallerContext? caller = context.InstanceServices.GetService<CallerContext>();

        // Scopes live in the AsyncLocal scope provider shared by every logger from the same
        // factory, so opening one on THIS logger attaches these fields to log lines written by
        // the services underneath too — not just to lines written here.
        using IDisposable? scope = _logger.BeginScope(
            new InvocationLogScope(context.FunctionDefinition.Name, context.InvocationId, caller));

        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception in {FunctionName}. InvocationId: {InvocationId}, UserId: {UserId}",
                context.FunctionDefinition.Name,
                context.InvocationId,
                caller?.UserId);

            HttpContext? http = context.GetHttpContext();

            // Every trigger in this app is an HttpTrigger, so this is belt-and-braces: a
            // non-HTTP trigger has no response to write, and swallowing there would hide the
            // failure from the host's own retry/poison handling.
            if (http is null) throw;

            await WriteErrorAsync(http, context.InvocationId, context.CancellationToken);
        }
    }

    /// <summary>
    /// Writes the 500 envelope, unless the response is already on the wire.
    /// Returns <c>false</c> when it declined to write.
    /// </summary>
    /// <remarks>
    /// The three Excel exports (<c>budget-planning/wfp/report/export</c>,
    /// <c>budget-planning/ppmp/report/export</c>, <c>purchase-requests/{id}/export</c>) stream
    /// file bytes. A failure partway through one means headers are already flushed and the status
    /// code can no longer be changed — assigning to <c>StatusCode</c> at that point throws, which
    /// would replace a logged failure with a second unlogged one. The download simply truncates;
    /// the log line written by the caller above is the record of it.
    /// </remarks>
    internal static async Task<bool> WriteErrorAsync(
        HttpContext http, string invocationId, CancellationToken cancellationToken)
    {
        if (http.Response.HasStarted) return false;

        http.Response.StatusCode  = StatusCodes.Status500InternalServerError;
        http.Response.ContentType = "application/json; charset=utf-8";

        ApiResponse<object> body = ApiResponse<object>.Fail(
            string.Format(ClientMessage, invocationId));

        await http.Response.WriteAsync(JsonSerializer.Serialize(body, Json), cancellationToken);
        return true;
    }
}
