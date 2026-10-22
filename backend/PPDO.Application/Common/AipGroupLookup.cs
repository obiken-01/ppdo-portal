using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Common;

/// <summary>
/// The one resolver that points the existing AIP Entry, division-submit and comment paths at an
/// open SAIP's working copy (v1.10.0 — PPDO-247, <c>docs/v1.10/SAIP_Spec.md</c> §4 "they take the
/// office's open SAIP's copy as their tree").
///
/// <para>
/// <b>Two questions, two methods.</b> A write names one node, so <see cref="FindGroupAsync"/>
/// resolves the group row that node hangs from — the copy's row when the node is a copy row, the
/// approved row otherwise. An office-level action (submit, the tree read for AIP Entry, the comment
/// list) names an office, so <see cref="WorkingGroupsAsync"/> answers "which tree is this office
/// working on": the open SAIP's copy if there is one, else the approved groups.
/// </para>
///
/// <para>
/// ⚠️ <b>Never widens a list read.</b> The approved groups always come through the filtered
/// <c>GetOffices…</c> reads; a copy is only ever reached by its own id or by its own office's open
/// SAIP. That is what keeps Annex B, the initial WFP, the GSO feed and the review lists on the
/// approved figures while an SAIP is open (spec §3).
/// </para>
///
/// <para>
/// No extra rule is needed to lock the approved tree while an SAIP is open: an SAIP starts only on a
/// Consolidated office, and <see cref="AipWriteGuard.CheckAsync{T}"/> already refuses every write to
/// a Consolidated group. The copy's groups carry the SAIP's state in their own
/// <c>WorkflowStatus</c> (kept in step by the submit paths), so the same guard locks the copy once
/// it is sent to PPDO.
/// </para>
/// </summary>
public static class AipGroupLookup
{
    /// <summary>
    /// The group row with this id: approved (through the filtered read) or, when that finds nothing,
    /// an SAIP copy row. ⚠️ Approved first, so every existing caller and every existing test mock
    /// sees exactly what it saw before; the copy lookup only runs on what used to be a 404.
    /// </summary>
    public static async Task<AipOffice?> FindGroupAsync(IAipRepository repo, int id, CancellationToken ct)
        => await repo.GetOfficeByIdAsync(id, ct) ?? await repo.GetDraftGroupByIdAsync(id, ct);

    /// <summary>
    /// The groups config office <paramref name="officeId"/> is working on in record
    /// <paramref name="aipRecordId"/>: its open SAIP's copy when there is one, else
    /// <paramref name="approved"/> (the caller's filtered read, passed in so the approved path costs
    /// no second query). <c>SaipId</c> is the open SAIP's id, or null on the approved tree.
    /// </summary>
    public static async Task<(List<AipOffice> Groups, int? SaipId)> WorkingGroupsAsync(
        IAipRepository repo, int aipRecordId, int officeId, IEnumerable<AipOffice> approved, CancellationToken ct)
    {
        IReadOnlyList<AipOffice> draft = await repo.GetDraftGroupsForOfficeAsync(aipRecordId, officeId, ct);
        return draft is { Count: > 0 }
            ? (draft.ToList(), draft[0].SaipId)
            : (approved.Where(o => o.OfficeId == officeId).ToList(), null);
    }
}
