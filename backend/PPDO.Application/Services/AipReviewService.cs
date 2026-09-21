using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IAipReviewService"/> (V18-54 / PPDO-72).</summary>
public sealed class AipReviewService : IAipReviewService
{
    private readonly IAipRepository            _aipRepo;
    private readonly IRepository<AipOffice>    _officeRepo;
    private readonly IOfficeRepository         _officeConfigRepo;
    private readonly IAipExpenditureRepository _expRepo;
    private readonly IPermissionService        _permissions;
    private readonly IAuditService             _audit;
    private readonly ILogger<AipReviewService> _logger;

    public AipReviewService(
        IAipRepository            aipRepo,
        IRepository<AipOffice>    officeRepo,
        IOfficeRepository         officeConfigRepo,
        IAipExpenditureRepository expRepo,
        IPermissionService        permissions,
        IAuditService             audit,
        ILogger<AipReviewService> logger)
    {
        _aipRepo          = aipRepo;
        _officeRepo       = officeRepo;
        _officeConfigRepo = officeConfigRepo;
        _expRepo          = expRepo;
        _permissions      = permissions;
        _audit            = audit;
        _logger           = logger;
    }

    // ── Return to the office (V18-54 / PPDO-72) ───────────────────────────────

    public Task<ServiceResult<AipSubmitResultDto>> ReturnToOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
        => MoveAsync(
            aipRecordId, officeId, caller,
            from:        AipWorkflowStatus.SubmittedToPpdo,
            target:      AipWorkflowStatus.ReturnedByPpdo,
            auditAction: AuditAction.ReturnByPpdo,
            refuse:      status => Refuse(status, "return"),
            logMessage:  "AIP returned to the office by PPDO.",
            ct);

    // ── Accept into the consolidated AIP (V18-56 / PPDO-74) ───────────────────

    public Task<ServiceResult<AipSubmitResultDto>> AcceptOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
        => MoveAsync(
            aipRecordId, officeId, caller,
            from:        AipWorkflowStatus.SubmittedToPpdo,
            target:      AipWorkflowStatus.Consolidated,
            auditAction: AuditAction.AcceptByPpdo,
            refuse:      status => Refuse(status, "accept"),
            logMessage:  "AIP office accepted into the consolidated AIP by PPDO.",
            ct);

    // ── Re-open an accepted office (added 2026-09-14, PPDO-73) ────────────────

    public Task<ServiceResult<AipSubmitResultDto>> ReopenOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
        => MoveAsync(
            aipRecordId, officeId, caller,
            from:        AipWorkflowStatus.Consolidated,
            target:      AipWorkflowStatus.ReturnedByPpdo,
            auditAction: AuditAction.ReopenByPpdo,
            refuse:      RefuseReopen,
            logMessage:  "Accepted AIP office re-opened and sent back by PPDO.",
            ct);

    // ── The one transition every action is ────────────────────────────────────

    /// <summary>
    /// Moves every group row of one office from <paramref name="from"/> to
    /// <paramref name="target"/>.
    ///
    /// <para>
    /// ↩️ Re-open joined return and accept here on 2026-09-14. It differs only in where it starts
    /// and how it refuses, which is why both are parameters rather than a third copy of the move.
    /// </para>
    ///
    /// <para>
    /// <b>⚠️ Return and accept are the same transition with a different destination</b>, and they
    /// are written as one so they cannot drift. They share a starting state, a scope rule, a
    /// refusal split, the group-wide move and the audit shape; the only differences are the target
    /// column value, the audit constant and one word in the refusal. Two copies would have been two
    /// places to fix the day the concurrency rule or the audit shape changes — and the pair are
    /// pressed side by side on one screen, where a divergence reads as a bug in whichever the
    /// reviewer used second.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<AipSubmitResultDto>> MoveAsync(
        int aipRecordId, int officeId, User caller,
        string from, string target, string auditAction,
        Func<string, ServiceResult<AipSubmitResultDto>> refuse, string logMessage,
        CancellationToken ct)
    {
        ReviewContext? ctx = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipSubmitResultDto>.NotFound(NotFoundMessage(aipRecordId, officeId));

        if (ctx.Record.Status != PlanningStatus.Draft)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                $"The FY {ctx.Record.FiscalYear} AIP is '{ctx.Record.Status}' and cannot be changed.");

        // ⚠️ Branch on the first row that is NOT at PPDO, not on Groups[0]. The rows move together
        // so they should agree — but if they ever disagree, the refusal must describe the row that
        // actually blocks the transition rather than whichever happened to be loaded first.
        AipOffice? blocking = ctx.Groups.FirstOrDefault(g => g.WorkflowStatus != from);

        if (blocking is not null)
            return refuse(blocking.WorkflowStatus);

        foreach (AipOffice group in ctx.Groups)
        {
            group.WorkflowStatus = target;
            await _officeRepo.UpdateAsync(group, ct);
        }
        await _officeRepo.SaveChangesAsync(ct);

        // ⚠️ ONE row for the transition, keyed on the first group and carrying the rest in its
        // payload — the same shape SubmitToPpdoAsync writes. An office holds several group rows
        // (decision 21), so a row-per-group would make "Show History" (PPDO-77) render one return
        // three times unless it grouped them back; matching the sibling means one read strategy
        // covers the whole chain.
        await _audit.LogAsync("aip_offices", ctx.Groups[0].Id, auditAction,
            new { WorkflowStatus = from },
            new
            {
                WorkflowStatus = target,
                GroupIds       = ctx.Groups.Select(g => g.Id).ToArray(),
            }, ct);

        _logger.LogInformation(
            logMessage + " AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "Groups: {GroupCount}, UserId: {UserId}",
            aipRecordId, officeId, ctx.Groups.Count, caller.Id);

        return ServiceResult<AipSubmitResultDto>.Ok(new AipSubmitResultDto(
            aipRecordId, officeId, target, ctx.Groups.Count));
    }

    // ── The review read (V18-56 / PPDO-74) ────────────────────────────────────

    public async Task<ServiceResult<AipOfficeReviewDto>> GetOfficeForReviewAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        // The same resolver the two actions use, so what a reviewer can read and what they can act
        // on cannot come apart.
        ReviewContext? ctx = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipOfficeReviewDto>.NotFound(NotFoundMessage(aipRecordId, officeId));

        // ⚠️ Sequential awaits, never Task.WhenAll — these share one DbContext, which is not
        // thread-safe (the GetStatsAsync production 500, CLAUDE.md).
        List<int> groupIds = ctx.Groups.Select(g => g.Id).ToList();
        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsAsync(groupIds, ct);

        // ⚠️ NO division filter. AipReadScope narrows the host office's programs to the caller's
        // own division on the entry page; a review is of the whole office, and applying it here
        // would drop programs from a review silently — which reads as the office having failed to
        // encode them rather than as a filter.
        List<int> programIds = programs.Select(p => p.Id).ToList();
        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsAsync(programIds, ct);

        List<int> projectIds = projects.Select(j => j.Id).ToList();
        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(projectIds, ct);

        // The form's Funding Source column (7). Scoped by record rather than by the activity ids
        // because it costs the same query either way — the same call the entry read makes.
        IReadOnlyList<AipActivityFundCodeDto> fundRows =
            await _expRepo.GetFundCodesByAipRecordAsync(aipRecordId, ct);

        Office? office = await _officeConfigRepo.GetByIdAsync(officeId, ct);

        return ServiceResult<AipOfficeReviewDto>.Ok(new AipOfficeReviewDto(
            aipRecordId,
            ctx.Record.FiscalYear,
            officeId,
            // ⚠️ Falls back to the AIP group's own name rather than to an empty string: this is
            // what the confirm dialog says out loud before an irreversible action, and "Accept ?"
            // is worse than a slightly less official name.
            office?.OfficeName ?? ctx.Groups[0].Name,
            office?.OfficeCode ?? string.Empty,
            // The office's shared state — the rows move together (decision 21).
            ctx.Groups[0].WorkflowStatus,
            activities.Count,
            AipTreeMapper.BuildOffices(
                ctx.Groups, programs, projects, activities,
                AipTreeMapper.GroupFundCodes(fundRows))));
    }

    // ── The activity modal's read (PPDO-79) ───────────────────────────────────

    public async Task<ServiceResult<AipActivityReviewDto>> GetActivityForReviewAsync(
        int activityId, User caller, CancellationToken ct = default)
    {
        // One sentence for "no such activity" and "not yours" alike (PPDO-46).
        string notFound = $"AIP activity {activityId} not found.";

        // ⚠️ Walks UP one row per level rather than loading the office's whole tree: the modal shows
        // one activity, and PPDO's own office holds hundreds. Sequential awaits — these share one
        // DbContext, which is not thread-safe (CLAUDE.md).
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(activityId, ct);
        if (activity is null) return ServiceResult<AipActivityReviewDto>.NotFound(notFound);

        AipProject? project = await _aipRepo.GetProjectByIdAsync(activity.ProjectId, ct);
        if (project is null) return ServiceResult<AipActivityReviewDto>.NotFound(notFound);

        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null) return ServiceResult<AipActivityReviewDto>.NotFound(notFound);

        // ⚠️ A group with no owning office is a legacy row the V18-32 backfill could not match. The
        // search renders those without a link, so nothing legitimate opens one here.
        AipOffice? group = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        if (group?.OfficeId is not int officeId)
            return ServiceResult<AipActivityReviewDto>.NotFound(notFound);

        // ⚠️ "Own office" is the caller's office id, never OfficeScope.Resolve — which would hand a
        // PPDO department head every office in the province. See the interface remarks.
        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        bool ownOffice   = caller.OfficeId == officeId;
        bool deptHead    = ownOffice && await _permissions.CanReviewBudgetPlanningAsync(caller, ct);
        if (!crossOffice && !deptHead)
            return ServiceResult<AipActivityReviewDto>.NotFound(notFound);

        AipRecord? record = await _aipRepo.GetByIntIdAsync(group.AipRecordId, ct);
        if (record is null) return ServiceResult<AipActivityReviewDto>.NotFound(notFound);

        IReadOnlyList<AipExpenditure> lines = await _expRepo.GetByActivityIdAsync(activityId, ct);

        // ⚠️ One query for every line's items, never one per line — the same shape the entry
        // endpoint uses, and skipped outright for an activity with no lines.
        ILookup<int, AipProcurementItem> itemsByLine = lines.Count == 0
            ? Array.Empty<AipProcurementItem>().ToLookup(i => i.ExpenditureId)
            : (await _expRepo.GetProcurementItemsByExpenditureIdsAsync(
                    lines.Select(l => l.Id).ToList(), ct))
                .ToLookup(i => i.ExpenditureId);

        IReadOnlyList<string> fundCodes = await _expRepo.GetFundCodesByActivityIdAsync(activityId, ct);
        Office? office = await _officeConfigRepo.GetByIdAsync(officeId, ct);

        // ⚠️ AipActivityReviewDto.CanEdit's four conditions, mirroring the write path: this office's
        // department head, a Draft record, work still in the office's hands (AipWriteGuard), and not
        // denied by ReviewerWriteGuard — the one that stops a holder of BOTH flags, who would
        // otherwise be offered an Edit button that answers 403.
        bool canEdit = deptHead
            && record.Status == PlanningStatus.Draft
            && AipWorkflowStatus.IsOfficeEditable(group.WorkflowStatus)
            && !await ReviewerWriteGuard.DeniesWriteAsync(caller, _permissions, ct);

        return ServiceResult<AipActivityReviewDto>.Ok(new AipActivityReviewDto(
            record.Id,
            record.FiscalYear,
            officeId,
            // Same fallback as the whole-office read: a slightly less official name beats a blank.
            office?.OfficeName ?? group.Name,
            office?.OfficeCode ?? string.Empty,
            group.RefCode,
            group.Sector,
            group.WorkflowStatus,
            new AipReviewPathNodeDto(program.Id, program.RefCode, program.Name),
            new AipReviewPathNodeDto(project.Id, project.RefCode, project.Name),
            AipTreeMapper.MapActivity(activity, fundCodes),
            lines.Select(l => AipExpenditureService.Map(l, itemsByLine[l.Id])).ToList(),
            canEdit));
    }

    // ── The query-first search (V18-75 / PPDO-76) ─────────────────────────────

    /// <summary>Page size ceiling. A client asking for more gets this; the grid pages instead.</summary>
    private const int MaxPageSize = 100;

    public async Task<ServiceResult<AipReviewSearchResultDto>> SearchAsync(
        AipReviewSearchRequestDto request, User caller, CancellationToken ct = default)
    {
        AipRecord? record = await _aipRepo.GetLatestByFiscalYearAsync(request.FiscalYear, ct);

        int page     = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize <= 0 ? 25 : request.PageSize, 1, MaxPageSize);

        // ⚠️ An unopened year is an EMPTY page, not a 404. The reviewer has done nothing wrong, and
        // the page has a state for "nothing to search yet" — turning it into an error would put a
        // red banner in front of a perfectly ordinary situation at the start of a season.
        if (record is null)
            return ServiceResult<AipReviewSearchResultDto>.Ok(new AipReviewSearchResultDto(
                0, request.FiscalYear, [], 0, page, pageSize,
                new Dictionary<string, int>(), new Dictionary<string, int>()));

        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);

        // ⚠️ CLAMP, never refuse. A guest office that asks about another office gets its own rows
        // back — a 403 would confirm the other office exists, which is the enumeration PPDO-46
        // closed off. The clamp reaches the repository as an ordinary one-value office filter.
        List<int> officeIds = crossOffice
            ? (request.OfficeIds ?? []).Distinct().ToList()
            : await OwnOfficeForSearchAsync(caller, ct);

        List<string> statuses = (request.WorkflowStatuses ?? []).ToList();

        // ⚠️ "Everything applicable to me", resolved HERE from the caller's own flags — never from
        // anything the client sent. For a cross-office reviewer the useful answer is their actual
        // queue: the offices sitting at PPDO waiting on a decision. For anybody else it is their
        // own office — which the clamp above has already applied, so "mine" adds nothing for them
        // and must not: re-adding the caller's office here is how a host-office non-reviewer,
        // clamped to nothing, would get PPDO's whole office back through a checkbox (PPDO-79).
        if (request.Mine && crossOffice && statuses.Count == 0)
            statuses.Add(AipWorkflowStatus.SubmittedToPpdo);

        // ⚠️ Short-circuit rather than querying: an empty office filter means "no filter" one layer
        // down, so falling through would show a caller clamped to nothing every office in the
        // province. Covers both a caller with no office (DECISION F) and a host non-reviewer.
        if (!crossOffice && officeIds.Count == 0)
            return ServiceResult<AipReviewSearchResultDto>.Ok(new AipReviewSearchResultDto(
                record.Id, record.FiscalYear, [], 0, page, pageSize,
                new Dictionary<string, int>(), new Dictionary<string, int>()));

        AipReviewSearchPage result = await _aipRepo.SearchReviewNodesAsync(
            new AipReviewSearchQuery(
                record.Id,
                officeIds,
                (request.Sectors ?? []).ToList(),
                statuses,
                SplitCodeList(request.RefCode),
                // ⚠️ Passed through whole. This is the field that must NOT be split.
                request.Title,
                Skip: (page - 1) * pageSize,
                Take: pageSize),
            ct);

        return ServiceResult<AipReviewSearchResultDto>.Ok(new AipReviewSearchResultDto(
            record.Id,
            record.FiscalYear,
            result.Items.Select(r => new AipReviewSearchRowDto(
                r.Level, r.NodeId, r.RefCode, r.Name,
                r.OfficeId, r.AipOfficeName, r.Sector, r.WorkflowStatus)).ToList(),
            result.TotalCount,
            page,
            pageSize,
            result.SectorCounts,
            result.WorkflowStatusCounts));
    }

    /// <summary>
    /// Splits a typed ref-code list on <c>OR</c> or a comma — <c>"1000-…-010 OR 3000-…-010"</c>.
    ///
    /// <para>
    /// <b>⚠️ A flat set, never a tree.</b> No precedence, no nesting, no <c>AND</c>: this is a
    /// separator, not an expression language (decision 16). If this method ever needs to return
    /// anything but a list, the decision has been reversed by accident.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>Code-shaped fields only.</b> The same splitting applied to the title field would break
    /// any project legitimately named "Aid or relief distribution" — which is why the title is
    /// passed through whole and this method is never called on it.
    /// </para>
    /// </summary>
    private static List<string> SplitCodeList(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split([" OR ", " or ", ","], StringSplitOptions.RemoveEmptyEntries)
                 .Select(v => v.Trim())
                 .Where(v => v.Length > 0)
                 .Distinct(StringComparer.OrdinalIgnoreCase)
                 .ToList();

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The one office a caller who is NOT a cross-office reviewer searches — or none (PPDO-79).
    ///
    /// <para>
    /// ⚠️ <b>Not <c>OfficeScope.Resolve</c>, and the difference is the whole method.</b> Resolve gives
    /// every host-office user the entire province. That was unreachable in practice while the search
    /// page was cross-office-only; PPDO-79 opened it to department heads, which put it one sidebar
    /// click from PPDO's own department head. This query also cannot apply the division axis that
    /// narrows a PPDO encoder everywhere else, so the host office is decided here explicitly:
    /// </para>
    /// <list type="bullet">
    ///   <item>Guest office — their own office. No division axis applies to them.</item>
    ///   <item>Host office, department head — PPDO's own office. The flag is office-scoped and they
    ///   review the whole of it, the same reason the review reads apply no division filter.</item>
    ///   <item>Host office, no reviewer flag — nothing. They hold no review work.</item>
    ///   <item>No office at all — nothing (DECISION F).</item>
    /// </list>
    /// </summary>
    private async Task<List<int>> OwnOfficeForSearchAsync(User caller, CancellationToken ct)
    {
        if (caller.OfficeId is not int own || own == OfficeScope.NoOffice) return [];

        if (!OfficeScope.Resolve(caller).SeeAll) return [own];

        return await _permissions.CanReviewBudgetPlanningAsync(caller, ct) ? [own] : [];
    }

    /// <summary>
    /// The refusal for an office that is not at PPDO, split by <b>who is responsible for that</b>.
    ///
    /// ⚠️ <c>Conflict</c> and <c>BadRequest</c> are not two spellings of "no". A 409 says another
    /// reviewer got there first and the reader's own view is stale — reload and look again. A 400
    /// says the work never came to PPDO at all, which is not a race and reloading will not change
    /// it. Answering both with 400 tells the reviewer whose colleague just acted that they
    /// themselves did something wrong.
    /// </summary>
    /// <param name="actionWord">
    /// "return" or "accept" — the only part of the refusal that differs between the two. ⚠️ Both
    /// actions share the 409 sentences verbatim, because the reviewer who lost the race lost it the
    /// same way whichever button they pressed.
    /// </param>
    private static ServiceResult<AipSubmitResultDto> Refuse(string status, string actionWord)
        => status switch
    {
        // Verbatim from spec §4.2.
        AipWorkflowStatus.ReturnedByPpdo => ServiceResult<AipSubmitResultDto>.Conflict(
            "This office was already returned by someone else. Reload to see the current state."),

        AipWorkflowStatus.Consolidated => ServiceResult<AipSubmitResultDto>.Conflict(
            $"This office was already accepted into {AipWriteGuard.Describe(AipWorkflowStatus.Consolidated)} "
            + "by someone else. Reload to see the current state."),

        // Draft and DepartmentReview, plus any state added later — closed is the safe default, and
        // "has not been sent to PPDO" is true of every state that has not reached PPDO.
        _ => ServiceResult<AipSubmitResultDto>.BadRequest(
            $"This office's AIP is in {AipWriteGuard.Describe(status)} and has not been sent to "
            + $"PPDO, so there is nothing to {actionWord}."),
    };

    /// <summary>
    /// The refusal for re-opening an office that is not accepted.
    ///
    /// ⚠️ <b>Always a 409.</b> The button is only offered on an accepted office, so any other state
    /// means the office moved while this screen was open — a colleague already sent it back, or it
    /// was re-opened and has moved on since. None of those is the reader's mistake.
    /// </summary>
    private static ServiceResult<AipSubmitResultDto> RefuseReopen(string status)
        => ServiceResult<AipSubmitResultDto>.Conflict(
            $"This office is no longer accepted — it is in {AipWriteGuard.Describe(status)}. "
            + "Reload to see the current state.");

    /// <summary>
    /// The record and every group row of the office being acted on — or null when this caller may
    /// not act on it.
    ///
    /// <para>
    /// ⚠️ <b>Two independent checks, and the office one is not enough on its own.</b>
    /// <see cref="OfficeScope.ResolveForReview"/> answers "may this caller see this office", and a
    /// user in the <b>host</b> office resolves to <c>All</c> on that axis regardless of any
    /// reviewer flag — so scope alone would let an ordinary PPDO division encoder return any
    /// office in the province. The reviewer grant is therefore checked in its own right. That is
    /// the "tempting wrong axis" the spec flags on the consolidated view (tracker B4): the gate is
    /// the flag, never <c>IsHostOffice</c>.
    /// </para>
    ///
    /// <para>
    /// ⚠️ Null for every refusal reason, so the caller cannot tell "not yours" from "no such
    /// office" (PPDO-46). The endpoint gates on the same flag and answers 403 first, so a caller
    /// who reaches the null-from-flag path here is holding a stale session or calling out of band.
    /// </para>
    /// </summary>
    private async Task<ReviewContext?> ResolveAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct)
    {
        AipRecord? record = await _aipRepo.GetByIntIdAsync(aipRecordId, ct);
        if (record is null) return null;

        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        if (!crossOffice) return null;

        if (!OfficeScope.ResolveForReview(caller, crossOffice).Permits(officeId)) return null;

        IReadOnlyList<AipOffice> all = await _aipRepo.GetOfficesByAipIdAsync(aipRecordId, ct);
        List<AipOffice> groups = all.Where(o => o.OfficeId == officeId).ToList();
        if (groups.Count == 0) return null;

        return new ReviewContext(record, officeId, groups);
    }

    /// <summary>One sentence for both "no such office" and "not yours" — see PPDO-46.</summary>
    private static string NotFoundMessage(int aipRecordId, int officeId)
        => $"AIP office {officeId} not found in record {aipRecordId}.";

    private sealed record ReviewContext(AipRecord Record, int OfficeId, List<AipOffice> Groups);
}
