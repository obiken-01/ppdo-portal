using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker.Http;
using PPDO.Application.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Functions.Functions;

/// <summary>
/// Shared helpers for the config CRUD endpoints (RAL-70): the <c>{ data, error, message }</c>
/// envelope, ServiceResult → HTTP status mapping, JSON options, and body/header reads.
/// </summary>
internal static class ConfigHttp
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy        = JsonNamingPolicy.CamelCase,
    };

    internal static string? AuthHeader(HttpRequestData req)
        => req.Headers.TryGetValues("Authorization", out IEnumerable<string>? values)
            ? values.FirstOrDefault()
            : null;

    /// <summary>Any authenticated user — used for reference-data list endpoints (dropdowns).</summary>
    internal static readonly Func<User, Task<bool>> Authenticated = _ => Task.FromResult(true);

    /// <summary>
    /// Validates the JWT and the supplied permission predicate.
    /// Returns the caller on success, or a 401/403 response to short-circuit on failure.
    /// </summary>
    internal static async Task<(User? caller, HttpResponseData? denied)> AuthorizeAsync(
        HttpRequestData req,
        IJwtMiddleware jwt,
        Func<User, Task<bool>> permit,
        CancellationToken cancellationToken)
    {
        User? caller = await jwt.ValidateAsync(AuthHeader(req), cancellationToken);
        if (caller is null)
            return (null, req.CreateResponse(HttpStatusCode.Unauthorized));
        if (!await permit(caller))
            return (null, req.CreateResponse(HttpStatusCode.Forbidden));
        return (caller, null);
    }

    internal static async Task<T?> ReadBodyAsync<T>(HttpRequestData req, CancellationToken cancellationToken)
    {
        try { return await JsonSerializer.DeserializeAsync<T>(req.Body, Json, cancellationToken); }
        catch { return default; }
    }

    internal static async Task<string> ReadTextAsync(HttpRequestData req)
    {
        using StreamReader reader = new(req.Body);
        return await reader.ReadToEndAsync();
    }

    internal static async Task<HttpResponseData> EnvelopeAsync<T>(
        HttpRequestData req, HttpStatusCode status, ApiResponse<T> body, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(status);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body, Json), cancellationToken);
        return response;
    }

    /// <summary>Maps a <see cref="ServiceResult{T}"/> to an enveloped HTTP response.</summary>
    internal static Task<HttpResponseData> FromResultAsync<T>(
        HttpRequestData req,
        ServiceResult<T> result,
        CancellationToken cancellationToken,
        HttpStatusCode okStatus = HttpStatusCode.OK,
        string? message = null)
    {
        if (result.IsSuccess)
            return EnvelopeAsync(req, okStatus, ApiResponse<T>.Ok(result.Value!, message), cancellationToken);

        HttpStatusCode status = result.Code switch
        {
            ServiceErrorCode.NotFound   => HttpStatusCode.NotFound,
            ServiceErrorCode.Conflict   => HttpStatusCode.Conflict,
            ServiceErrorCode.BadRequest => HttpStatusCode.BadRequest,
            ServiceErrorCode.Forbidden  => HttpStatusCode.Forbidden,
            _                           => HttpStatusCode.InternalServerError,
        };
        return EnvelopeAsync(req, status, ApiResponse<T>.Fail(result.Error ?? "An unexpected error occurred."), cancellationToken);
    }

    /// <summary>Returns a CSV file response (text/csv + attachment filename).</summary>
    internal static async Task<HttpResponseData> CsvFileAsync(
        HttpRequestData req, string csv, string fileName, CancellationToken cancellationToken)
    {
        HttpResponseData response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/csv; charset=utf-8");
        response.Headers.Add("Content-Disposition", $"attachment; filename=\"{fileName}\"");
        await response.WriteStringAsync(csv, cancellationToken);
        return response;
    }
}
