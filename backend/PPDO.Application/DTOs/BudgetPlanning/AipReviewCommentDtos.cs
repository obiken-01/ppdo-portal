using PPDO.Domain.Enums;

namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// What a client sends to leave a comment (V18-53 / PPDO-71).
///
/// ⚠️ <see cref="NodeType"/> is a <b>string</b>, not the enum. The shared serializer has no
/// <c>JsonStringEnumConverter</c> — adding one would silently change every existing endpoint's
/// enum output from a number to a name — so DTOs here carry enum <i>names</i> and the service
/// parses them, exactly as <c>UserResponseDto.Role</c> does.
/// </summary>
/// <param name="NodeType">Which kind of row — <c>Program</c>, <c>Project</c> or <c>Activity</c>.</param>
/// <param name="NodeId">That row's id.</param>
/// <param name="Body">The remark. 1–2,000 characters.</param>
public sealed record CreateAipReviewCommentDto(
    string NodeType,
    int    NodeId,
    string Body);

/// <summary>
/// One comment as rendered (V18-53 / PPDO-71).
/// </summary>
/// <param name="AuthorSide">
/// Which side wrote it. ⚠️ The value stored on the comment, not re-derived from the author's
/// current flags — see <see cref="AipCommentSide"/>.
/// </param>
/// <param name="CanResolve">
/// Whether <b>this caller</b> may resolve it. ⚠️ Computed per request, and false for the person the
/// comment is addressed to however senior they are: only the authoring side resolves. The UI hides
/// the control on false, and the endpoint refuses it too — the flag is a convenience, never the
/// enforcement.
/// </param>
/// <param name="IsOrphaned">
/// True when the row this was anchored to no longer exists. ⚠️ Not an error and not a reason to
/// drop the comment: it is part of the record of why the work changed, so it renders as orphaned
/// rather than disappearing with the row that provoked it.
/// </param>
public sealed record AipReviewCommentDto(
    int       Id,
    int       AipOfficeId,
    string    NodeType,
    int       NodeId,
    string?   NodeRefCode,
    string    Body,
    Guid      AuthorId,
    string    AuthorName,
    string    AuthorSide,
    DateTime  CreatedAt,
    DateTime? ResolvedAt,
    string?   ResolvedByName,
    bool      CanResolve,
    bool      IsOrphaned);

/// <summary>
/// The unresolved tally, split by who wrote them (V18-53 / PPDO-71).
///
/// <para>
/// <b>⚠️ Two numbers, never one.</b> There are exactly two authoring sides — the encoder never
/// comments — and merging them would imply an action the reader does not have: they cannot resolve
/// either set themselves. "3 unresolved from PPDO" is also the sentence that actually changes
/// whether someone re-submits, which a single total is not.
/// </para>
/// </summary>
public sealed record AipUnresolvedCountsDto(
    int FromDepartmentHead,
    int FromPpdo)
{
    /// <summary>For the re-submit warning's headline. The split is still what it renders beneath.</summary>
    public int Total => FromDepartmentHead + FromPpdo;
}

/// <summary>Every comment on one office, plus the tally the re-submit gate warns with.</summary>
/// <param name="CanComment">
/// Whether this caller may leave a comment at all. ⚠️ False for an encoder — they read comments,
/// act on them and re-submit, but never write one. The endpoint refuses independently; this only
/// decides whether the composer is offered.
/// </param>
public sealed record AipReviewCommentsDto(
    int                                AipRecordId,
    int                                OfficeId,
    IReadOnlyList<AipReviewCommentDto> Comments,
    AipUnresolvedCountsDto             Unresolved,
    bool                               CanComment);
