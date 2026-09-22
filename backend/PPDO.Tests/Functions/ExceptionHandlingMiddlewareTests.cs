using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using PPDO.Application.Common;
using PPDO.Functions.Middleware;

namespace PPDO.Tests.Functions;

/// <summary>
/// Tests for the worker exception stage (PPDO-43).
///
/// <para>Split deliberately. <see cref="ExceptionHandlingMiddleware.Invoke"/> needs a real
/// <c>FunctionContext</c> and the <c>GetHttpContext()</c> extension, which reads a framework-
/// internal key out of <c>FunctionContext.Items</c> — mocking that pins the test to an
/// implementation detail of the worker SDK. So the decisions worth protecting live in
/// <see cref="ExceptionHandlingMiddleware.WriteErrorAsync"/> and
/// <see cref="InvocationLogScope"/>, both of which take plain inputs and are tested here
/// against the real <see cref="DefaultHttpContext"/> rather than a mock of it.</para>
/// </summary>
public class ExceptionHandlingMiddlewareTests
{
    private const string InvocationId = "f1e2d3c4-0000-4000-8000-abcdefabcdef";

    private static DefaultHttpContext ContextWithBodyStream()
    {
        DefaultHttpContext http = new();
        http.Response.Body = new MemoryStream();
        return http;
    }

    private static string ReadBody(HttpContext http)
    {
        http.Response.Body.Position = 0;
        return new StreamReader(http.Response.Body, Encoding.UTF8).ReadToEnd();
    }

    // ── WriteErrorAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task WriteErrorAsync_FreshResponse_Writes500()
    {
        DefaultHttpContext http = ContextWithBodyStream();

        bool wrote = await ExceptionHandlingMiddleware.WriteErrorAsync(http, InvocationId, default);

        Assert.True(wrote);
        Assert.Equal(StatusCodes.Status500InternalServerError, http.Response.StatusCode);
        Assert.Equal("application/json; charset=utf-8", http.Response.ContentType);
    }

    [Fact]
    public async Task WriteErrorAsync_FreshResponse_WritesTheStandardEnvelope()
    {
        DefaultHttpContext http = ContextWithBodyStream();

        await ExceptionHandlingMiddleware.WriteErrorAsync(http, InvocationId, default);

        // Same { data, error, message } shape ConfigHttp.EnvelopeAsync produces, camelCased —
        // a 500 that arrived in a different shape would defeat the frontend's error reader.
        using JsonDocument doc = JsonDocument.Parse(ReadBody(http));
        JsonElement root = doc.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("data").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("message").ValueKind);
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("error").GetString()));
    }

    [Fact]
    public async Task WriteErrorAsync_FreshResponse_QuotesTheInvocationIdForSupport()
    {
        DefaultHttpContext http = ContextWithBodyStream();

        await ExceptionHandlingMiddleware.WriteErrorAsync(http, InvocationId, default);

        using JsonDocument doc = JsonDocument.Parse(ReadBody(http));
        Assert.Contains(InvocationId, doc.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData("Exception")]
    [InlineData("StackTrace")]
    [InlineData("SqlConnection")]
    [InlineData("   at PPDO.")]
    public async Task WriteErrorAsync_FreshResponse_LeaksNoInternals(string forbidden)
    {
        DefaultHttpContext http = ContextWithBodyStream();

        await ExceptionHandlingMiddleware.WriteErrorAsync(http, InvocationId, default);

        Assert.DoesNotContain(forbidden, ReadBody(http), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WriteErrorAsync_ResponseAlreadyStarted_WritesNothingAndDoesNotThrow()
    {
        // The Excel exports stream file bytes, so a failure partway through one leaves headers
        // already flushed. Assigning StatusCode then throws — which would turn one logged
        // failure into a second unlogged one. The stage must decline instead.
        DefaultHttpContext http = ContextWithBodyStream();
        http.Features.Set<IHttpResponseFeature>(new StartedResponseFeature());
        http.Response.StatusCode = StatusCodes.Status200OK;

        bool wrote = await ExceptionHandlingMiddleware.WriteErrorAsync(http, InvocationId, default);

        Assert.False(wrote);
        Assert.Equal(StatusCodes.Status200OK, http.Response.StatusCode);
        Assert.Equal(string.Empty, ReadBody(http));
    }

    // ── InvocationLogScope ────────────────────────────────────────────────────

    [Fact]
    public void InvocationLogScope_ReadsUserIdOnEnumeration_NotAtConstruction()
    {
        // The reason this type exists. The scope opens BEFORE the handler runs, and
        // CallerContext.UserId is only set partway through it by the JWT check. A dictionary
        // scope would capture null here and report null forever.
        CallerContext caller = new();
        InvocationLogScope scope = new("GetAipRecord", InvocationId, caller);

        Assert.Equal(InvocationLogScope.Anonymous, Lookup(scope, "UserId"));

        Guid userId = Guid.NewGuid();
        caller.SetUserId(userId);

        Assert.Equal(userId.ToString(), Lookup(scope, "UserId"));
    }

    [Fact]
    public void InvocationLogScope_CarriesFunctionNameAndInvocationId()
    {
        InvocationLogScope scope = new("GetAipRecord", InvocationId, new CallerContext());

        Assert.Equal("GetAipRecord", Lookup(scope, "FunctionName"));
        Assert.Equal(InvocationId, Lookup(scope, "InvocationId"));
    }

    [Fact]
    public void InvocationLogScope_WithNoCallerContext_ReportsAnonymous()
    {
        // context.InstanceServices.GetService<CallerContext>() can return null — the scope must
        // still open rather than taking down every request with a NullReferenceException.
        InvocationLogScope scope = new("Health", InvocationId, caller: null);

        Assert.Equal(InvocationLogScope.Anonymous, Lookup(scope, "UserId"));
        Assert.Equal(3, scope.Count);
    }

    [Fact]
    public void InvocationLogScope_ToString_IncludesTheCallerForProvidersThatDoNotEnumerate()
    {
        CallerContext caller = new();
        Guid userId = Guid.NewGuid();
        caller.SetUserId(userId);

        string rendered = new InvocationLogScope("GetAipRecord", InvocationId, caller).ToString();

        Assert.Contains("GetAipRecord", rendered);
        Assert.Contains(InvocationId, rendered);
        Assert.Contains(userId.ToString(), rendered);
    }

    private static string? Lookup(InvocationLogScope scope, string key) =>
        scope.First(pair => pair.Key == key).Value?.ToString();

    /// <summary>
    /// Minimal response feature reporting <c>HasStarted = true</c>. DefaultHttpContext's own
    /// feature always reports false and there is no setter, so the streaming case is only
    /// reachable by substituting the feature.
    /// </summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public Stream Body { get; set; } = new MemoryStream();
        public bool HasStarted => true;
        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
        public string? ReasonPhrase { get; set; }
        public int StatusCode { get; set; } = StatusCodes.Status200OK;

        public void OnCompleted(Func<object, Task> callback, object state) { }
        public void OnStarting(Func<object, Task> callback, object state) { }
    }
}
