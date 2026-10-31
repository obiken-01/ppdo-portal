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

    /// <summary>
    /// PPMP report (v1.5). Hidden in v1.9.1 (PPDO-236), when we thought nobody used it.
    /// ↩️ <b>Restored in v1.9.2 (PPDO-265):</b> a PPDO colleague builds the office's PPMP from this
    /// report's list of procurement items per activity, and uses it in production today. It is PPDO
    /// only: the routes are gated on <c>CanAccessWfp</c>, because the report reads PPDO's WFP.
    /// While false, both <c>/api/budget-planning/ppmp/report/*</c> routes return 404 before reading
    /// anything. Keep it in step with <c>PPMP_REPORT_ENABLED</c> in <c>frontend/src/lib/ppmp.ts</c>.
    /// </summary>
    public static readonly bool PpmpReportEnabled = true;
}
