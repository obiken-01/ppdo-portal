namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// What a rejected save tells the encoder (V18-71 / PPDO-118). Rides in
/// <c>ServiceResult.Details</c> and is serialized as the <c>data</c> of the 409 envelope; see
/// <c>docs/v1.8/AIP_Concurrent_Edit_Spec.md</c> §4.
///
/// <para>
/// The fields answer the three questions someone whose save was just refused actually has:
/// <b>who</b> changed it, <b>when</b>, and <b>what it says now</b> — so the UI can render a
/// side-by-side compare without a second round trip.
/// </para>
///
/// <para>
/// Generic so <paramref name="Current"/> stays typed per surface
/// (<c>AipConflictDto&lt;AipActivityDto&gt;</c>, <c>AipConflictDto&lt;AipExpenditureDto&gt;</c>)
/// rather than degrading to <c>object</c> at the construction site.
/// </para>
/// </summary>
/// <param name="ChangedByName">
/// Display name of whoever saved last. ⚠️ <b>Null is a real case the UI must handle</b> — the row
/// may never have been edited since PPDO-117 shipped (no <c>updated_by_id</c> yet), or the user
/// may no longer resolve. A conflict that cannot be attributed is still a conflict, and reporting
/// it anonymously beats not reporting it.
/// </param>
/// <param name="ChangedAtUtc">
/// When the winning save happened, <b>UTC</b>. Render as UTC+8 (<c>CLAUDE.md</c>). Null under the
/// same conditions as <paramref name="ChangedByName"/>.
/// </param>
/// <param name="CurrentRowVersion">
/// The row's version <i>now</i>, base64. ⚠️ This is what <b>Overwrite</b> resubmits with, and it is
/// what makes that action one request rather than reload-then-save. Drop it and the UI cannot
/// offer Overwrite at all.
/// </param>
/// <param name="Current">The row as it now stands, for the compare.</param>
public record AipConflictDto<T>(
    string?   ChangedByName,
    DateTime? ChangedAtUtc,
    string    CurrentRowVersion,
    T         Current);
