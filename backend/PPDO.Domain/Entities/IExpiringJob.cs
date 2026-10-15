namespace PPDO.Domain.Entities;

/// <summary>
/// A job kind that leaves a file behind for a limited time (v1.9.0 — PPDO-228, spec decisions 6 and 12).
/// Lets the generic expiry query (<c>IBackgroundJobRepository.GetExpiredAsync</c>) work for any such
/// kind without knowing which one it is.
/// </summary>
public interface IExpiringJob
{
    /// <summary>The <c>IFileStore</c> key of the job's output, or null when it has none (yet).</summary>
    string? FileKey { get; set; }

    /// <summary>When the output is deleted (UTC), or null while there is no output.</summary>
    DateTime? ExpiresAt { get; set; }
}
