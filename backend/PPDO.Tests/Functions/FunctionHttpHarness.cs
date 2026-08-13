using System.Collections.Specialized;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Moq;

namespace PPDO.Tests.Functions;

/// <summary>
/// Minimal in-memory <see cref="HttpRequestData"/>/<see cref="HttpResponseData"/> pair so Function
/// handlers can be exercised directly in xUnit — the isolated worker has no test host, and the
/// handlers' security-relevant logic (JWT check → permission check → division clamp) lives above
/// the service layer, so it is unreachable from the Application-layer tests.
///
/// Deliberately tiny: only the members the handlers touch are real (Url/Query, Headers, Body,
/// CreateResponse). Cookies and identities are unused and throw/return empty rather than pretend.
/// </summary>
internal static class FunctionHttp
{
    /// <summary>
    /// Builds a GET request; <paramref name="query"/> is appended verbatim (no leading "?").
    /// The path only matters for readability — handlers route by attribute and read
    /// <see cref="HttpRequestData.Query"/>, never the path itself.
    /// </summary>
    internal static FakeHttpRequestData Get(
        string query,
        string? authorizationHeader = "Bearer test-token",
        string path = "budget-planning/ppmp/report/preview")
    {
        Uri url = new($"https://localhost/api/{path}?{query}");
        return new FakeHttpRequestData(new Mock<FunctionContext>().Object, url, authorizationHeader);
    }

    /// <summary>Reads a response body back as text (rewinds first — handlers leave it at the end).</summary>
    internal static string BodyText(HttpResponseData response)
    {
        response.Body.Position = 0;
        using StreamReader reader = new(response.Body, Encoding.UTF8, leaveOpen: true);
        return reader.ReadToEnd();
    }

    /// <summary>Reads a response body back as bytes (the .xlsx export path).</summary>
    internal static byte[] BodyBytes(HttpResponseData response)
    {
        response.Body.Position = 0;
        using MemoryStream buffer = new();
        response.Body.CopyTo(buffer);
        return buffer.ToArray();
    }

    /// <summary>First value of a response header, or null when absent.</summary>
    internal static string? Header(HttpResponseData response, string name)
        => response.Headers.TryGetValues(name, out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;
}

internal sealed class FakeHttpRequestData : HttpRequestData
{
    public FakeHttpRequestData(FunctionContext functionContext, Uri url, string? authorizationHeader)
        : base(functionContext)
    {
        Url     = url;
        Headers = new HttpHeadersCollection();
        if (!string.IsNullOrEmpty(authorizationHeader))
            Headers.Add("Authorization", authorizationHeader);
    }

    public override Stream Body { get; } = new MemoryStream();
    public override HttpHeadersCollection Headers { get; }
    public override IReadOnlyCollection<IHttpCookie> Cookies { get; } = Array.Empty<IHttpCookie>();
    public override Uri Url { get; }
    public override IEnumerable<ClaimsIdentity> Identities { get; } = Array.Empty<ClaimsIdentity>();
    public override string Method => "GET";
    public override NameValueCollection Query => System.Web.HttpUtility.ParseQueryString(Url.Query);

    public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);
}

internal sealed class FakeHttpResponseData : HttpResponseData
{
    public FakeHttpResponseData(FunctionContext functionContext) : base(functionContext) { }

    public override HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;
    public override HttpHeadersCollection Headers { get; set; } = new();
    public override Stream Body { get; set; } = new MemoryStream();

    public override HttpCookies Cookies => throw new NotSupportedException("Cookies are unused by these handlers.");
}
