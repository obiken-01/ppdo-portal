namespace PPDO.Domain.Entities;

/// <summary>
/// Generic application-level audit trail covering all budget planning entities (RAL-77).
/// One row per CREATE / UPDATE / DELETE action, with JSON snapshots of the changed fields.
/// RecordId is not a real FK — it points into whichever table TableName names.
/// </summary>
public sealed class AuditLog
{
    /// <summary>Primary key (BIGINT IDENTITY).</summary>
    public long Id { get; set; }

    /// <summary>Physical table name — e.g. "aip_activities", "wfp_expenditure_lines". Max 100 characters.</summary>
    public string TableName { get; set; } = string.Empty;

    /// <summary>PK of the affected row in TableName.</summary>
    public int RecordId { get; set; }

    /// <summary>"CREATE", "UPDATE", or "DELETE". Max 10 characters.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>FK to the user who made the change.</summary>
    public Guid ChangedById { get; set; }

    /// <summary>UTC timestamp of the change.</summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>JSON snapshot of the changed fields before the action. Null for CREATE.</summary>
    public string? OldValues { get; set; }

    /// <summary>JSON snapshot of the changed fields after the action. Null for DELETE.</summary>
    public string? NewValues { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>The user who made the change.</summary>
    public User? ChangedBy { get; set; }
}
