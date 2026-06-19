namespace PPDO.Application.Common;

/// <summary>
/// Standard config-API response envelope (RAL-70): <c>{ data, error, message }</c>.
///   data    — payload on success (null on error).
///   error   — human-readable error on failure (null on success).
///   message — optional informational message (e.g. CSV upload summary).
/// </summary>
public sealed record ApiResponse<T>(T? Data, string? Error, string? Message)
{
    public static ApiResponse<T> Ok(T data, string? message = null) => new(data, null, message);
    public static ApiResponse<T> Fail(string error) => new(default, error, null);
}
