namespace PPDO.Domain.Enums;

/// <summary>
/// Which kind of AIP tree node a comment is anchored to (v1.8.0 Phase 4 — V18-53 / PPDO-71).
///
/// <para>
/// <b>⚠️ A comment anchors to a ROW, not to a field</b> (<c>AIP_Review_Spec.md</c> decision 7). The
/// AIP grid is wide, and field-level anchoring would multiply anchor points for no reviewer
/// benefit — a reviewer says "this activity is wrong", not "cell MOOE of this activity is wrong".
/// </para>
///
/// <para>
/// <b>⚠️ Expenditure and procurement lines are deliberately absent.</b> They are the contents of an
/// activity row rather than rows of the tree, and the spec's anchor is the tree row. A comment on a
/// costing belongs on its activity.
/// </para>
/// </summary>
public enum AipCommentNodeType
{
    /// <summary><c>aip_programs.id</c>.</summary>
    Program = 0,

    /// <summary><c>aip_projects.id</c>.</summary>
    Project = 1,

    /// <summary><c>aip_activities.id</c>.</summary>
    Activity = 2,
}
