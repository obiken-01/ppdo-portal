using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IAipSubmitService"/> (V18-42 / PPDO-52, V18-49 / PPDO-59).</summary>
public sealed class AipSubmitService : IAipSubmitService
{
    private readonly IAipRepository            _aipRepo;
    private readonly IAipExpenditureRepository _expRepo;
    private readonly IAipCeilingService        _ceiling;
    private readonly IRepository<AipOffice>    _officeRepo;
    private readonly IAuditService             _audit;
    private readonly ILogger<AipSubmitService> _logger;

    public AipSubmitService(
        IAipRepository            aipRepo,
        IAipExpenditureRepository expRepo,
        IAipCeilingService        ceiling,
        IRepository<AipOffice>    officeRepo,
        IAuditService             audit,
        ILogger<AipSubmitService> logger)
    {
        _aipRepo    = aipRepo;
        _expRepo    = expRepo;
        _ceiling    = ceiling;
        _officeRepo = officeRepo;
        _audit      = audit;
        _logger     = logger;
    }

    // ── Readiness ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipReadinessDto>> GetReadinessAsync(
        int aipRecordId, User caller, CancellationToken ct = default)
    {
        ReadinessContext? ctx = await ResolveAsync(aipRecordId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipReadinessDto>.NotFound($"AIP record {aipRecordId} not found.");

        return ServiceResult<AipReadinessDto>.Ok(await BuildAsync(ctx, ct));
    }

    // ── Submit ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipSubmitResultDto>> SubmitAsync(
        int aipRecordId, User caller, CancellationToken ct = default)
    {
        ReadinessContext? ctx = await ResolveAsync(aipRecordId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipSubmitResultDto>.NotFound($"AIP record {aipRecordId} not found.");

        if (ctx.Record.Status != PlanningStatus.Draft)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                $"The FY {ctx.Record.FiscalYear} AIP is '{ctx.Record.Status}' and cannot be submitted.");

        // ⚠️ Already-submitted is refused by naming the state, not by silently succeeding. A second
        // submit is almost always a double-click or a stale tab, and "it worked" would be a lie.
        List<AipOffice> notDraft = ctx.Groups
            .Where(g => g.WorkflowStatus != AipWorkflowStatus.Draft)
            .ToList();
        if (notDraft.Count > 0)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                $"This office's AIP is already in {AipWriteGuard.Describe(notDraft[0].WorkflowStatus)} "
                + "and cannot be submitted again.");

        // ⚠️ Re-run rather than trust a readiness call the client made a moment ago. Between the
        // two, PBO can cut the ceiling and a colleague can delete the last line off an activity.
        AipReadinessDto readiness = await BuildAsync(ctx, ct);
        if (!readiness.CanSubmit)
            return ServiceResult<AipSubmitResultDto>.BadRequest(FormatRefusal(readiness));

        foreach (AipOffice group in ctx.Groups)
        {
            group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;
            await _officeRepo.UpdateAsync(group, ct);
        }
        await _officeRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("aip_offices", ctx.Groups[0].Id, AuditAction.SubmitToDeptHead,
            new { WorkflowStatus = AipWorkflowStatus.Draft },
            new
            {
                WorkflowStatus = AipWorkflowStatus.DepartmentReview,
                GroupIds       = ctx.Groups.Select(g => g.Id).ToArray(),
            }, ct);

        _logger.LogInformation(
            "AIP submitted for department review. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "Groups: {GroupCount}, Activities: {ActivityCount}",
            aipRecordId, ctx.OfficeId, ctx.Groups.Count, readiness.ActivityCount);

        return ServiceResult<AipSubmitResultDto>.Ok(new AipSubmitResultDto(
            aipRecordId, ctx.OfficeId, AipWorkflowStatus.DepartmentReview, ctx.Groups.Count));
    }

    // ── Submit onward to PPDO (V18-51 / PPDO-69) ──────────────────────────────

    /// <summary>
    /// The states this hop may start from. <c>ReturnedByPpdo</c> is here because a re-submit after
    /// PPDO sends work back is the <b>same</b> transition, not a third one — the office fixed what
    /// was asked and the department head sends it on again.
    /// </summary>
    private static readonly string[] SubmittableToPpdo =
    [
        AipWorkflowStatus.DepartmentReview,
        AipWorkflowStatus.ReturnedByPpdo,
    ];

    public async Task<ServiceResult<AipSubmitResultDto>> SubmitToPpdoAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        ReadinessContext? ctx = await ResolveAsync(aipRecordId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipSubmitResultDto>.NotFound($"AIP record {aipRecordId} not found.");

        // ⚠️ NotFound, not Forbidden, and the same sentence either way: an office the caller does
        // not own must be indistinguishable from one that does not exist (PPDO-46). ResolveAsync
        // has already narrowed to the caller's own office, so a mismatch here means they asked
        // about someone else's — or their own office changed under a stale tab.
        if (ctx.Groups.Count == 0 || ctx.OfficeId != officeId)
            return ServiceResult<AipSubmitResultDto>.NotFound(
                $"AIP office {officeId} not found in record {aipRecordId}.");

        if (ctx.Record.Status != PlanningStatus.Draft)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                $"The FY {ctx.Record.FiscalYear} AIP is '{ctx.Record.Status}' and cannot be submitted.");

        // ⚠️ Refused by naming the state, including the case that looks like success. Submitting an
        // office already at PPDO is a double-click or a stale tab; answering "done" would tell the
        // department head their edits went on when they did not.
        List<AipOffice> wrongState = ctx.Groups
            .Where(g => !SubmittableToPpdo.Contains(g.WorkflowStatus))
            .ToList();
        if (wrongState.Count > 0)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                ctx.Groups[0].WorkflowStatus == AipWorkflowStatus.Draft
                    ? "This office's AIP has not been submitted for department review yet, so it "
                      + "cannot be sent on to PPDO."
                    : $"This office's AIP is in {AipWriteGuard.Describe(wrongState[0].WorkflowStatus)} "
                      + "and cannot be sent to PPDO from there.");

        // ⚠️ Run the whole checklist again. The department head may edit values during review
        // (spec decision 4), so an edit made in good faith can push the office over its ceiling or
        // strip the last line off an activity — and this is the last gate before PPDO sees it.
        AipReadinessDto readiness = await BuildAsync(ctx, ct);
        if (!readiness.CanSubmit)
            return ServiceResult<AipSubmitResultDto>.BadRequest(FormatRefusal(readiness));

        // Captured before the loop: after it, every row reads SubmittedToPpdo and the state the
        // office came from — which is what makes a re-submit legible in the history — is gone.
        string from = ctx.Groups[0].WorkflowStatus;

        foreach (AipOffice group in ctx.Groups)
        {
            group.WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;
            await _officeRepo.UpdateAsync(group, ct);
        }
        await _officeRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("aip_offices", ctx.Groups[0].Id, AuditAction.SubmitToPpdo,
            new { WorkflowStatus = from },
            new
            {
                WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo,
                GroupIds       = ctx.Groups.Select(g => g.Id).ToArray(),
            }, ct);

        _logger.LogInformation(
            "AIP submitted to PPDO. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, From: {From}, "
            + "Groups: {GroupCount}, Activities: {ActivityCount}",
            aipRecordId, ctx.OfficeId, from, ctx.Groups.Count, readiness.ActivityCount);

        return ServiceResult<AipSubmitResultDto>.Ok(new AipSubmitResultDto(
            aipRecordId, ctx.OfficeId, AipWorkflowStatus.SubmittedToPpdo, ctx.Groups.Count));
    }

    // ── The checklist ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs every check across the office's whole subtree. Four queries regardless of size —
    /// programs, projects, activities, line counts — never one per node.
    /// </summary>
    private async Task<AipReadinessDto> BuildAsync(ReadinessContext ctx, CancellationToken ct)
    {
        List<int> groupIds = ctx.Groups.Select(g => g.Id).ToList();

        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsAsync(groupIds, ct);
        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList(), ct);
        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(projects.Select(p => p.Id).ToList(), ct);

        IReadOnlyList<AipActivityLineCountDto> counts =
            await _expRepo.CountByActivityIdsAsync(activities.Select(a => a.Id).ToList(), ct);
        Dictionary<int, AipActivityLineCountDto> countsByActivity =
            counts.ToDictionary(c => c.ActivityId);

        List<AipReadinessIssueDto> issues = [];

        foreach (AipActivity activity in activities)
        {
            // Absent means zero — CountByActivityIdsAsync omits activities with no lines.
            AipActivityLineCountDto? counted = countsByActivity.GetValueOrDefault(activity.Id);
            int lineCount = counted?.LineCount ?? 0;

            if (lineCount == 0)
            {
                // ⚠️ Two states, same line count, opposite meanings (V18-34). Total is NULL for an
                // activity that was never costed and 0 for one whose lines were all deleted. Both
                // block submit, but the encoder needs to know which — "add the costing" and "you
                // deleted the costing" send them to different places.
                bool costedAtZero = activity.Total is not null;
                issues.Add(new AipReadinessIssueDto(
                    costedAtZero ? "costed-at-zero" : "no-lines",
                    activity.Id, activity.RefCode,
                    costedAtZero
                        ? $"'{activity.Name}' has no expenditure lines left — its costing was removed. "
                          + "Add a line or delete the activity."
                        : $"'{activity.Name}' has not been costed yet. Add at least one expenditure line."));
            }
            else if (activity.Total is null or 0m)
            {
                issues.Add(new AipReadinessIssueDto(
                    "zero-total", activity.Id, activity.RefCode,
                    $"'{activity.Name}' has expenditure lines but totals ₱0. Enter the amounts."));
            }

            // ⚠️ A line with no funding source is INVISIBLE to the ceiling check, which sums
            // General Fund only — a null fund is not the General Fund. Without this an office can
            // encode any amount against no fund, satisfy "has at least one line", contribute
            // nothing to its ceiling and submit cleanly. Found by live-testing, not review.
            if (counted is { LinesWithoutFund: > 0 })
                issues.Add(new AipReadinessIssueDto(
                    "missing-fund", activity.Id, activity.RefCode,
                    $"'{activity.Name}' has {counted.LinesWithoutFund} expenditure line"
                    + $"{(counted.LinesWithoutFund == 1 ? "" : "s")} with no funding source. "
                    + "A line without a fund is not counted against any ceiling."));

            if (string.IsNullOrWhiteSpace(activity.EsreCode))
                issues.Add(new AipReadinessIssueDto(
                    "missing-esre", activity.Id, activity.RefCode,
                    $"'{activity.Name}' has no eSRE code."));

            if (string.IsNullOrWhiteSpace(activity.CcTypologyCode))
                issues.Add(new AipReadinessIssueDto(
                    "missing-cc-typology", activity.Id, activity.RefCode,
                    $"'{activity.Name}' has no climate-change typology code."));
        }

        // ⚠️ An office with nothing in it is not "ready" — it is empty. Submitting it would hand a
        // department head a blank document with no indication anything was wrong.
        if (activities.Count == 0)
            issues.Add(new AipReadinessIssueDto(
                "empty", null, null,
                "There is nothing to submit yet — this office has no activities."));

        // The ceiling is checked once for the office, not per group: the bound is the office's own
        // General Fund ceiling (V18-46).
        AipCeilingStatusDto? ceiling = null;
        if (ctx.Groups.Count > 0)
        {
            ceiling = await _ceiling.GetStatusAsync(ctx.Groups[0].Id, ct);
            if (await _ceiling.ValidateForSubmitAsync(ctx.Groups[0].Id, ct) is string ceilingError)
                issues.Add(new AipReadinessIssueDto("ceiling", null, null, ceilingError));
        }

        return new AipReadinessDto(
            ctx.Record.Id, ctx.OfficeId,
            ctx.Groups.Count > 0 ? ctx.Groups[0].WorkflowStatus : AipWorkflowStatus.Draft,
            CanSubmit: issues.Count == 0,
            ActivityCount: activities.Count,
            Issues: issues,
            Ceiling: ceiling);
    }

    /// <summary>
    /// The refusal body. ⚠️ A <b>list</b>, one entry per failing node, never a single sentence
    /// (spec §4 error shapes) — and capped, because an office that has costed nothing would
    /// otherwise produce hundreds of lines nobody reads.
    /// </summary>
    private static string FormatRefusal(AipReadinessDto readiness)
    {
        const int shown = 10;
        IEnumerable<string> lines = readiness.Issues.Take(shown).Select(i => "• " + i.Message);
        string body = string.Join("\n", lines);

        int remaining = readiness.Issues.Count - shown;
        if (remaining > 0)
            body += $"\n• …and {remaining} more.";

        return "This AIP cannot be submitted yet:\n" + body;
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves the record and <b>every</b> group row belonging to the caller's office.
    ///
    /// ⚠️ Scoped by the caller's own office, never by a supplied id — submit is an act on your own
    /// work. A caller with no office resolves to <c>OfficeScope.NoOffice</c> and matches nothing,
    /// which is the DECISION F rule: unassigned sees nothing rather than everything.
    /// </summary>
    private async Task<ReadinessContext?> ResolveAsync(
        int aipRecordId, User caller, CancellationToken ct)
    {
        AipRecord? record = await _aipRepo.GetByIntIdAsync(aipRecordId, ct);
        if (record is null) return null;

        OfficeScope scope = OfficeScope.Resolve(caller);

        IReadOnlyList<AipOffice> all = await _aipRepo.GetOfficesByAipIdAsync(aipRecordId, ct);
        List<AipOffice> mine = all.Where(o => scope.Permits(o.OfficeId)).ToList();

        // A host-office user legitimately sees every office. Submitting is still an act on ONE
        // office's work, so narrow to the caller's own — otherwise a PPDO admin pressing submit
        // would move the entire province at once.
        if (caller.OfficeId is int callerOfficeId)
            mine = mine.Where(o => o.OfficeId == callerOfficeId).ToList();

        return new ReadinessContext(record, caller.OfficeId ?? 0, mine);
    }

    private sealed record ReadinessContext(AipRecord Record, int OfficeId, List<AipOffice> Groups);
}
