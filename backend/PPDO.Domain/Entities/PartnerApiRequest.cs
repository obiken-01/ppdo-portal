namespace PPDO.Domain.Entities;

/// <summary>
/// One logged call against <c>/api/external/v1</c> (v1.8.0 — PPDO-15/PPDO-13). Separate from
/// <see cref="AuditLog"/> because <c>AuditLog.ChangedById</c> is a required user id and a partner
/// system is not a user — issuing and revoking a <see cref="PartnerApiKey"/> is still an
/// <see cref="AuditLog"/> entry, against the admin who did it, but a partner's data pull is not.
///
/// Written only for calls that pass authentication — <c>200</c>, <c>400</c>, <c>403</c> — per
/// build spec §3.1: a <c>401</c> has no key to attribute it to, and a <c>429</c> is a flood, not a
/// distinct call worth a row.
/// </summary>
public sealed class PartnerApiRequest
{
    /// <summary>Primary key (BIGINT IDENTITY) — expected to accumulate faster than most tables here.</summary>
    public long Id { get; set; }

    /// <summary>FK to the key that made this call.</summary>
    public int KeyId { get; set; }

    public DateTime RequestedAt { get; set; }

    /// <summary>Route called, e.g. "aip", "aip/fiscal-years". Max 100 characters.</summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>The <c>officeCode</c> query parameter as requested, or null for a whole-year call. Max 20 chars.</summary>
    public string? OfficeCode { get; set; }

    /// <summary>The <c>fiscalYear</c> query parameter, when the route takes one.</summary>
    public int? FiscalYear { get; set; }

    /// <summary>HTTP status code returned for this call.</summary>
    public int StatusCode { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public PartnerApiKey? Key { get; set; }
}
