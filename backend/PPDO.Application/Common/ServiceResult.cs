namespace PPDO.Application.Common;

/// <summary>
/// Discriminated result returned by Application services to avoid exception-based
/// flow control. The Function handler switches on <see cref="Code"/> to select the
/// appropriate HTTP status code.
/// </summary>
public sealed class ServiceResult<T>
{
    /// <summary>The operation result value. Non-null only when <see cref="IsSuccess"/> is true.</summary>
    public T? Value { get; }

    /// <summary>True when the operation completed successfully.</summary>
    public bool IsSuccess => Code == ServiceErrorCode.None;

    /// <summary>Human-readable error description. Non-null only when <see cref="IsSuccess"/> is false.</summary>
    public string? Error { get; }

    /// <summary>Machine-readable error category for HTTP status mapping.</summary>
    public ServiceErrorCode Code { get; }

    private ServiceResult(T? value, string? error, ServiceErrorCode code)
    {
        Value = value;
        Error = error;
        Code  = code;
    }

    // ── Factory methods ────────────────────────────────────────────────────────

    public static ServiceResult<T> Ok(T value)
        => new(value, null, ServiceErrorCode.None);

    public static ServiceResult<T> NotFound(string error)
        => new(default, error, ServiceErrorCode.NotFound);

    public static ServiceResult<T> Forbidden(string error)
        => new(default, error, ServiceErrorCode.Forbidden);

    public static ServiceResult<T> Conflict(string error)
        => new(default, error, ServiceErrorCode.Conflict);

    public static ServiceResult<T> BadRequest(string error)
        => new(default, error, ServiceErrorCode.BadRequest);
}

/// <summary>Machine-readable error categories for HTTP status mapping.</summary>
public enum ServiceErrorCode
{
    None       = 0,
    NotFound   = 1,
    Forbidden  = 2,
    Conflict   = 3,
    BadRequest = 4,
}
