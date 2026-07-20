namespace PPDO.Application.Common;

/// <summary>
/// Simple on/off switches for features that aren't ready for their full RBAC treatment yet.
/// Flip a value here and adjust the corresponding <c>PermissionService</c> method to expand
/// (or restrict) access — no Function/frontend changes needed.
/// </summary>
public static class FeatureFlags
{
    /// <summary>
    /// Gates the Audit Log config page (<c>/config/audit-log</c>). Currently SuperAdmin-only
    /// via <c>PermissionService.CanViewAuditLogAsync</c> — flip to false to hide it entirely,
    /// or extend that method's role check when other roles should see it.
    /// </summary>
    // static readonly (not const) so flipping it doesn't produce an "unreachable code"
    // compiler warning at every call site that checks it.
    public static readonly bool AuditLogPageEnabled = true;
}
