using PPDO.Domain.Entities;

namespace PPDO.Application.Common;

/// <summary>
/// What the division lock needs to know about one office for one caller (v1.8.0 — PPDO-148,
/// <c>docs/v1.8/Division_Submit_Spec.md</c> decisions 2, 5, 6 and "Further rules").
///
/// <para>
/// ⚠️ <b>Pure on purpose.</b> Every rule below is a function of this object and nothing else, so
/// the rules are tested without a database or a mock (<c>AipDivisionContextTests</c>, and the
/// division rows of <c>PermissionMatrixTests</c>). <see cref="IAipDivisionLock"/> builds it; the
/// rules never reach back into a repository.
/// </para>
///
/// <para>
/// ⚠️ <b>This only ever REFUSES.</b> It is checked after <see cref="AipWriteGuard.CheckAsync{T}"/>
/// has passed, never instead of it, so the division rule cannot grant a write that the office's
/// workflow state refuses (spec §3.3 "Office lock unchanged"). A department head is exempt from
/// the division rule. They are not exempt from the office rule.
/// </para>
/// </summary>
public sealed class AipDivisionContext
{
    private readonly IReadOnlyDictionary<int, Division> _divisions;
    private readonly IReadOnlySet<int> _submitted;

    /// <param name="hasDivisions">
    /// The office uses the division flow: an FY2028+ record (decision 12) and at least one
    /// <b>active</b> division (decision 4). False means today's behaviour, untouched.
    /// </param>
    /// <param name="isDepartmentHead">
    /// Admin/SuperAdmin, or the office's own <c>CanReviewBudgetPlanning</c> holder. Exempt from the
    /// division rule (decision 2) and the only caller who chooses or changes a tag (decision 5).
    /// </param>
    /// <param name="callerDivisionId">
    /// The caller's division, but only when it is an active division of THIS office. A PPDO
    /// Staff member looking at a guest office has a division, just not one here, and is treated
    /// exactly like an encoder with none.
    /// </param>
    /// <param name="divisions">Every division of the office, inactive included, for names.</param>
    /// <param name="submittedDivisionIds">Divisions of this office whose row reads Submitted.</param>
    public AipDivisionContext(
        bool hasDivisions,
        bool isDepartmentHead,
        int? callerDivisionId,
        IReadOnlyDictionary<int, Division> divisions,
        IReadOnlySet<int> submittedDivisionIds)
    {
        HasDivisions     = hasDivisions;
        IsDepartmentHead = isDepartmentHead;
        CallerDivisionId = callerDivisionId;
        _divisions       = divisions;
        _submitted       = submittedDivisionIds;
    }

    /// <summary>An office outside the division flow — every rule below answers "allowed".</summary>
    public static AipDivisionContext None { get; } = new(
        false, false, null, new Dictionary<int, Division>(), new HashSet<int>());

    public bool HasDivisions     { get; }
    public bool IsDepartmentHead { get; }
    public int? CallerDivisionId { get; }

    /// <summary>The division's display name, or null for an untagged activity or an unknown id.</summary>
    public string? NameOf(int? divisionId)
        => divisionId is int id && _divisions.TryGetValue(id, out Division? d) ? d.Name : null;

    public bool IsSubmitted(int divisionId) => _submitted.Contains(divisionId);

    // ── Messages (spec §3.1 and §3.3 — read by encoders, keep them in their words) ──

    public const string NotAssignedMessage =
        "You are not assigned to a division in this office. Ask your department head to assign you one.";

    public const string ChooseDivisionMessage = "Choose the division this activity belongs to.";

    public const string DivisionNotInOfficeMessage = "That division is not part of this office.";

    public const string DivisionInactiveMessage = "That division is inactive.";

    public const string UntaggedActivityMessage =
        "This activity has no division yet. Ask your department head to assign it to one.";

    public string LockedMessage(int divisionId)
        => $"{NameOf(divisionId) ?? "This division"}'s work has been submitted to the department head "
           + "and can no longer be edited here.";

    public string BelongsToMessage(int divisionId)
        => $"This activity belongs to {NameOf(divisionId) ?? "another division"}.";

    // ── Rules ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Any write in the office that is not about one activity: containers (program, project,
    /// office group). Only the encoder with no division here is refused — they are read-only
    /// (spec "Further rules"). Renaming or adding to a container is otherwise open.
    /// </summary>
    public string? RefuseContainerWrite()
        => HasDivisions && !IsDepartmentHead && CallerDivisionId is null ? NotAssignedMessage : null;

    /// <summary>
    /// Editing or deleting one existing activity, or any of its expenditure lines (spec §3.3).
    ///
    /// ⚠️ An <b>untagged</b> activity is refused to encoders too. It is nobody's division's work
    /// yet, so "their own" does not cover it, and letting any encoder edit it would make the tag
    /// the department head is about to set meaningless. The department head tags it first.
    /// </summary>
    public string? RefuseActivityWrite(int? activityDivisionId)
    {
        if (!HasDivisions || IsDepartmentHead) return null;
        if (CallerDivisionId is not int own) return NotAssignedMessage;
        if (activityDivisionId is not int tagged) return UntaggedActivityMessage;
        if (tagged != own) return BelongsToMessage(tagged);
        if (IsSubmitted(own)) return LockedMessage(own);
        return null;
    }

    public bool CanWriteActivity(int? activityDivisionId) => RefuseActivityWrite(activityDivisionId) is null;

    /// <summary>
    /// The division a NEW activity is saved with (decision 5), or the refusal.
    ///
    /// <list type="bullet">
    /// <item>No division flow — untagged, and anything the client sent is ignored.</item>
    /// <item>Encoder — always their own division; the client value is ignored. Adding to a division
    /// that has already been submitted is an edit of locked work, so it is refused.</item>
    /// <item>Department head / Admin — must name an active division of this office. A submitted one
    /// is fine: they edit locked work (decision 2).</item>
    /// </list>
    /// </summary>
    public (int? DivisionId, string? Refusal) ResolveNewActivityTag(int? requestedDivisionId)
    {
        if (!HasDivisions) return (null, null);

        if (!IsDepartmentHead)
        {
            if (CallerDivisionId is not int own) return (null, NotAssignedMessage);
            if (IsSubmitted(own)) return (null, LockedMessage(own));
            return (own, null);
        }

        if (requestedDivisionId is not int requested) return (null, ChooseDivisionMessage);
        return RefuseTarget(requested) is string bad ? (null, bad) : (requested, null);
    }

    /// <summary>A department head's chosen division: this office's, and active (spec §3.1).</summary>
    public string? RefuseTarget(int divisionId)
    {
        if (!_divisions.TryGetValue(divisionId, out Division? d)) return DivisionNotInOfficeMessage;
        if (!d.IsActive) return DivisionInactiveMessage;
        return null;
    }

    /// <summary>
    /// Deleting a program, project or office group (spec "Further rules"). The delete cascades to
    /// every activity underneath, so an encoder may only delete a container whose activities are
    /// all their own and still open.
    /// </summary>
    /// <param name="nodeLabel">"program", "project" or "office" — read by a person.</param>
    public string? RefuseContainerDelete(IEnumerable<int?> activityDivisionIds, string nodeLabel)
    {
        if (RefuseContainerWrite() is string notAssigned) return notAssigned;
        if (!HasDivisions || IsDepartmentHead) return null;

        int own = CallerDivisionId!.Value;
        List<int?> tags = activityDivisionIds.ToList();

        if (tags.Any(t => t is int other && other != own))
            return $"This {nodeLabel} contains another division's activities.";
        if (tags.Any(t => t is null))
            return $"This {nodeLabel} contains activities with no division. "
                   + "Ask your department head to assign them first.";
        if (tags.Count > 0 && IsSubmitted(own))
            return $"This {nodeLabel} contains {NameOf(own) ?? "your division"}'s submitted activities.";
        return null;
    }
}
