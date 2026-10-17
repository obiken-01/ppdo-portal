namespace PPDO.Domain.Enums;

/// <summary>
/// Which side wrote an Investment Proposal comment (v1.9.0 — PPDO-219,
/// <c>docs/v1.9/Investment_Proposal_Comments_Spec.md</c> decision 4).
///
/// <para>
/// <b>⚠️ Stored on the comment, never re-derived from the author's office at read time.</b> People
/// move between offices and gain or lose flags; if the side were derived, an old comment would
/// silently change who may resolve it. Only the authoring side resolves — otherwise the office
/// could clear PPDO's remark and the Finalize gate would be self-marking.
/// </para>
///
/// <para>Stored as the member name (<c>nvarchar(10)</c>), so a hand-run query reads <c>Ppdo</c>, not <c>1</c>.</para>
/// </summary>
public enum ProposalCommentSide
{
    /// <summary>The caller belongs to the proposal's own office.</summary>
    Office = 0,

    /// <summary>Anyone else who can read the proposal: a PPDO cross-office reviewer or a host Admin.</summary>
    Ppdo = 1,
}
