namespace PPDO.Application.DTOs.BudgetPlanning;

/// <summary>
/// What the AIP Review search page sends (v1.8.0 Phase 4 — V18-75 / PPDO-76,
/// <c>AIP_Review_Spec.md</c> §4.1).
///
/// <para>
/// <b>⚠️ Fields AND together; values within a field OR.</b> That is the whole combination model —
/// there is no boolean expression language, no precedence and no nesting (decision 16). If a
/// request here ever needs a parser, the decision has been reversed by accident.
/// </para>
///
/// <para>
/// ⚠️ Everything here is <b>raw</b>: the OR-list splitting and the scope clamp happen in the
/// service, not on the client and not in the repository.
/// </para>
/// </summary>
/// <param name="RefCode">
/// One or more ref-code <b>prefixes</b>, typed as a flat list separated by <c>OR</c> or a comma.
/// ⚠️ Matched anchored (<c>LIKE 'x%'</c>). "One office, all sectors" is <b>not</b> expressible here
/// — sector is segment 1 and office is segment 5 — which is why <see cref="OfficeIds"/> exists
/// alongside it rather than as a convenience.
/// </param>
/// <param name="Title">
/// Free text over a program / project / activity name. ⚠️ <b>Never OR-split</b>: a title may
/// legitimately contain the word "or".
/// </param>
/// <param name="Mine">
/// "Everything applicable to me", resolved from the caller's own permissions — see
/// <c>AipReviewService.SearchAsync</c>. ⚠️ Never a client-supplied role.
/// </param>
public sealed record AipReviewSearchRequestDto(
    int                   FiscalYear,
    IReadOnlyList<int>?   OfficeIds,
    IReadOnlyList<string>? Sectors,
    IReadOnlyList<string>? WorkflowStatuses,
    string?               RefCode,
    string?               Title,
    bool                  Mine,
    int                   Page,
    int                   PageSize);

/// <summary>
/// One result row — a program, project or activity (PPDO-76).
///
/// <para>
/// ⚠️ <b>Slim, and deliberately so.</b> No amounts, no expected outputs, no descriptions: the grid
/// renders none of them, and a fat AIP DTO once produced a 1.2 MB response
/// (<c>docs/PERFORMANCE_GUIDELINES.md</c>). A reviewer who wants the figures opens the row.
/// </para>
/// </summary>
/// <param name="Level">
/// <c>Program</c>, <c>Project</c> or <c>Activity</c> — the same vocabulary
/// <c>AipReviewCommentDto.NodeType</c> uses, so the client keys both off one union type.
/// </param>
/// <param name="OfficeId">
/// The config office the row belongs to — <b>what the result link opens</b>. Null only for a legacy
/// row the V18-32 backfill could not match, which the client renders as unlinkable rather than
/// dropping.
/// </param>
public sealed record AipReviewSearchRowDto(
    string  Level,
    int     NodeId,
    string  RefCode,
    string  Name,
    int?    OfficeId,
    string  OfficeName,
    string  Sector,
    string  WorkflowStatus);

/// <summary>
/// A page of results with the counts the filter chips render (PPDO-76).
///
/// <para>
/// ⚠️ <b><see cref="TotalCount"/> is the size of the whole match, not of this page</b> — otherwise
/// the pager reads "1 of 1" on every page and a reviewer never learns there is more.
/// </para>
///
/// <para>
/// ⚠️ Each count set is computed with <b>its own</b> field's filter removed. Counting with every
/// filter applied would show the selected chip its own total and every sibling zero, which turns
/// "Select multiple to combine" into a lie.
/// </para>
/// </summary>
/// <param name="AipRecordId">
/// The record the results came from — the client needs it to link a row into the review screen, and
/// it resolved the fiscal year rather than the id.
/// </param>
public sealed record AipReviewSearchResultDto(
    int                                    AipRecordId,
    int                                    FiscalYear,
    IReadOnlyList<AipReviewSearchRowDto>   Items,
    int                                    TotalCount,
    int                                    Page,
    int                                    PageSize,
    IReadOnlyDictionary<string,int>        SectorCounts,
    IReadOnlyDictionary<string,int>        WorkflowStatusCounts);
