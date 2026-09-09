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
            target:      AipWorkflowStatus.ReturnedByPpdo,
            auditAction: AuditAction.ReturnByPpdo,
            actionWord:  "return",
            logMessage:  "AIP returned to the office by PPDO.",
            ct);

    // ── Accept into the consolidated AIP (V18-56 / PPDO-74) ───────────────────

    public Task<ServiceResult<AipSubmitResultDto>> AcceptOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
        => MoveAsync(
            aipRecordId, officeId, caller,
            target:      AipWorkflowStatus.Consolidated,
            auditAction: AuditAction.AcceptByPpdo,
            actionWord:  "accept",
            logMessage:  "AIP office accepted into the consolidated AIP by PPDO.",
            ct);

    // ── The one transition both actions are ───────────────────────────────────

    /// <summary>
    /// Moves every group row of one office from <c>SubmittedToPpdo</c> to
    /// <paramref name="target"/>.
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
        string target, string auditAction, string actionWord, string logMessage,
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
        AipOffice? blocking = ctx.Groups.FirstOrDefault(
            g => g.WorkflowStatus != AipWorkflowStatus.SubmittedToPpdo);

        if (blocking is not null)
            return Refuse(blocking.WorkflowStatus, actionWord);

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
            new { WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo },
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

    // ── Internals ─────────────────────────────────────────────────────────────

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
