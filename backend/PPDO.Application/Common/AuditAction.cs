namespace PPDO.Application.Common;

/// <summary>
/// String constants for the audit_log.action column (max 10 chars, matches DB constraint).
/// </summary>
public static class AuditAction
{
    public const string Create = "CREATE";
    public const string Update = "UPDATE";
    public const string Delete = "DELETE";
}
