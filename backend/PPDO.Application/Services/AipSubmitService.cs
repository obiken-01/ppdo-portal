using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Implementation of <see cref="IAipSubmitService"/> (V18-42 / PPDO-52, V18-49 / PPDO-59).
///
/// <para>
/// ⚠️ <b>Readiness and submit are deliberately NOT division-scoped</b> — <see cref="ResolveAsync"/>
/// and <see cref="BuildAsync"/> count every activity in the caller's whole office, unlike
/// <see cref="AipReadScope"/>'s entry-page program filter. A division-scoped encoder can therefore
/// see fewer programs on the entry page than "N activities in this office" counts. That mismatch
/// pre-dates PPDO-134 for host-office callers and was accepted, not fixed, by it: submitting is an
/// act on the WHOLE office's work, not one division's, so readiness answering for the office as a
/// unit is correct — checked and left alone when the division axis was extended to guest offices.
/// </para>
///
/// <para>
/// ↩️ <b>PPDO-149 adds the division flow</b> (<c>Division_Submit_Spec.md</c>). In an office that
/// uses it, each division submits its own tagged activities to the department head, and the office
/// moves to <c>DepartmentReview</c> by itself when the last division with activities has submitted
/// (decision 9). The office-level submit is refused there. The office's workflow state is still the
/// one every reader keys on — the division rows sit beside it, they do not replace it.
/// </para>
/// </summary>
public sealed class AipSubmitService : IAipSubmitService
{
    private readonly IAipRepository                   _aipRepo;
    private readonly IAipExpenditureRepository        _expRepo;
    private readonly IAipCeilingService               _ceiling;
    private readonly IRepository<AipOffice>           _officeRepo;
    private readonly IAuditService                    _audit;
    private readonly IAipDivisionSubmissionRepository _divRepo;
    private readonly IAipDivisionWorkflow             _divisions;
    private readonly IPermissionService               _permissions;
    private readonly IUserRepository                  _userRepo;
    private readonly ILogger<AipSubmitService>        _logger;

    public AipSubmitService(
        IAipRepository                   aipRepo,
        IAipExpenditureRepository        expRepo,
        IAipCeilingService               ceiling,
        IRepository<AipOffice>           officeRepo,
        IAuditService                    audit,
        IAipDivisionSubmissionRepository divRepo,
        IAipDivisionWorkflow             divisions,
        IPermissionService               permissions,
        IUserRepository                  userRepo,
        ILogger<AipSubmitService>        logger)
    {
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
        _ceiling     = ceiling;
        _officeRepo  = officeRepo;
        _audit       = audit;
        _divRepo     = divRepo;
        _divisions   = divisions;
        _permissions = permissions;
        _userRepo    = userRepo;
        _logger      = logger;
    }

    // ── Readiness ─────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipReadinessDto>> GetReadinessAsync(
        int aipRecordId, User caller, CancellationToken ct = default)
    {
        ReadinessContext? ctx = await ResolveAsync(aipRecordId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipReadinessDto>.NotFound($"AIP record {aipRecordId} not found.");

        return ServiceResult<AipReadinessDto>.Ok((await BuildAsync(ctx, ct)).Readiness);
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

        // ⚠️ PPDO-149 (§3.2). An office with divisions submits BY division. Checked before every
        // other rule so the answer names the right route rather than a readiness gap the division
        // flow would report differently.
        if (ctx.Groups.Count > 0 && await _divisions.LoadAsync(ctx.Record, ctx.OfficeId, ct) is not null)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                "This office submits by division. Each division head submits their own division.");

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
        AipReadinessDto readiness = (await BuildAsync(ctx, ct)).Readiness;
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

        // ⚠️ Over the ceiling does not stop this hop (PPDO-146, decision 13). It is carried back so
        // the encoder is told as they send it, and it blocks one hop later, at SubmitToPpdoAsync.
        if (readiness.CeilingWarning is not null)
            _logger.LogWarning(
                "AIP submitted for department review over its ceiling. AipRecordId: {AipRecordId}, "
                + "OfficeId: {OfficeId}, UserId: {UserId}",
                aipRecordId, ctx.OfficeId, caller.Id);

        return ServiceResult<AipSubmitResultDto>.Ok(new AipSubmitResultDto(
            aipRecordId, ctx.OfficeId, AipWorkflowStatus.DepartmentReview, ctx.Groups.Count,
            readiness.CeilingWarning));
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
        //
        // ↩️ PPDO-146: this is now the ONLY hop that enforces the ceiling. The encoder's submit
        // lets an over-ceiling office through with a warning, so CanSubmitToPpdo, not CanSubmit.
        //
        // ↩️ PPDO-149: in an office with divisions, every division with activities must have
        // submitted first. That is checked here too, and named first in the refusal: a
        // ReturnedByPpdo office reaches this hop while its divisions are still resubmitting, and
        // a department head re-tag can move work into a division that has not submitted.
        Checklist checklist = await BuildAsync(ctx, ct);
        AipReadinessDto readiness = checklist.Readiness;
        if (!readiness.CanSubmitToPpdo)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                FormatRefusal(readiness, includeCeiling: true, untaggedCount: checklist.UntaggedCount));

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

    // ── Return to the encoders (added 2026-09-14 with PPDO-73) ────────────────

    public async Task<ServiceResult<AipSubmitResultDto>> ReturnToEncoderAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        ReadinessContext? ctx = await ResolveAsync(aipRecordId, caller, ct);
        if (ctx is null)
            return ServiceResult<AipSubmitResultDto>.NotFound($"AIP record {aipRecordId} not found.");

        // Same rule and the same sentence as SubmitToPpdoAsync: an office the caller does not own is
        // indistinguishable from one that does not exist (PPDO-46).
        if (ctx.Groups.Count == 0 || ctx.OfficeId != officeId)
            return ServiceResult<AipSubmitResultDto>.NotFound(
                $"AIP office {officeId} not found in record {aipRecordId}.");

        if (ctx.Record.Status != PlanningStatus.Draft)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                $"The FY {ctx.Record.FiscalYear} AIP is '{ctx.Record.Status}' and cannot be changed.");

        // ⚠️ From department review ONLY (decided 2026-09-14). Returned-by-PPDO work is already
        // editable by encoders and department head alike, so there is nothing to hand down; work at
        // PPDO or accepted is not the department head's to move.
        AipOffice? blocking = ctx.Groups.FirstOrDefault(
            g => g.WorkflowStatus != AipWorkflowStatus.DepartmentReview);
        if (blocking is not null)
            return ServiceResult<AipSubmitResultDto>.BadRequest(
                blocking.WorkflowStatus == AipWorkflowStatus.Draft
                    ? "This office's AIP is already with its encoders."
                    : $"This office's AIP is in {AipWriteGuard.Describe(blocking.WorkflowStatus)}. It can "
                      + "only be returned to the encoders while it is in department review.");

        // ↩️ PPDO-149 (§3.4 "Return all"): in an office with divisions this hands EVERY submitted
        // division back too. Without it the office would sit in Draft with every division still
        // locked, and nobody but the department head could touch any of it.
        AipDivisionOfficeState? divisions = await _divisions.LoadAsync(ctx.Record, ctx.OfficeId, ct);
        IReadOnlyList<AipDivisionSubmission> returned =
            divisions is null ? [] : _divisions.StageReturnAll(divisions, caller.Id);

        // ⚠️ No completeness or ceiling re-run — those gate work moving forward. Sending it back
        // down must not be blocked by the very gaps it is being sent back to fix.
        foreach (AipOffice group in ctx.Groups)
        {
            group.WorkflowStatus = AipWorkflowStatus.Draft;
            await _officeRepo.UpdateAsync(group, ct);
        }
        // One save for the office rows and the division rows: they share the DbContext, so the
        // two cannot land apart.
        await _officeRepo.SaveChangesAsync(ct);

        // One row for the transition, the same shape as every other hand-off (spec §5.2).
        await _audit.LogAsync("aip_offices", ctx.Groups[0].Id, AuditAction.ReturnToEncoder,
            new { WorkflowStatus = AipWorkflowStatus.DepartmentReview },
            new
            {
                WorkflowStatus = AipWorkflowStatus.Draft,
                GroupIds       = ctx.Groups.Select(g => g.Id).ToArray(),
            }, ct);
        await _divisions.AuditReturnsAsync(returned, "department-head-return-all", ct);

        _logger.LogInformation(
            "AIP returned to the encoders by the department head. AipRecordId: {AipRecordId}, "
            + "OfficeId: {OfficeId}, Groups: {GroupCount}, DivisionsReturned: {DivisionsReturned}, UserId: {UserId}",
            aipRecordId, ctx.OfficeId, ctx.Groups.Count, returned.Count, caller.Id);

        return ServiceResult<AipSubmitResultDto>.Ok(new AipSubmitResultDto(
            aipRecordId, ctx.OfficeId, AipWorkflowStatus.Draft, ctx.Groups.Count));
    }

    // ── Division submit (PPDO-149) ────────────────────────────────────────────

    public async Task<ServiceResult<AipDivisionStatusListDto>> GetDivisionsAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        string notFound = $"AIP office {officeId} not found in record {aipRecordId}.";
        AipRecord? record = await _aipRepo.GetByIntIdAsync(aipRecordId, ct);
        if (record is null) return ServiceResult<AipDivisionStatusListDto>.NotFound(notFound);

        // A read, so the review scope: the office's own people, and whoever may review it — the
        // department head's panel and the PPDO board both show it (spec §6.2, §6.3). Acting on it is
        // narrower and is worked out per division below.
        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        if (!OfficeScope.ResolveForReview(caller, crossOffice).Permits(officeId))
            return ServiceResult<AipDivisionStatusListDto>.NotFound(notFound);

        List<AipOffice> groups = (await _aipRepo.GetOfficesByAipIdAsync(aipRecordId, ct))
            .Where(o => o.OfficeId == officeId).ToList();
        if (groups.Count == 0) return ServiceResult<AipDivisionStatusListDto>.NotFound(notFound);

        string officeStatus = groups[0].WorkflowStatus;
        AipDivisionOfficeState? state = await _divisions.LoadAsync(record, officeId, ct);
        if (state is null)
            return ServiceResult<AipDivisionStatusListDto>.Ok(
                new AipDivisionStatusListDto(officeId, officeStatus, false, 0, []));

        OfficeTree tree = await LoadTreeAsync(groups, ct);
        int untagged = tree.Activities.Count(a => a.DivisionId is null);

        IReadOnlyDictionary<Guid, string> names = await _userRepo.GetNamesByIdsAsync(
            state.Rows.Where(r => r.SubmittedById is not null).Select(r => r.SubmittedById!.Value)
                .Distinct().ToList(), ct);

        // Acting needs the caller's OWN office, the same rule as every other hand-off.
        bool ownOffice = caller.OfficeId == officeId;
        bool head      = ownOffice && await IsDepartmentHeadAsync(caller, ct);
        bool reviewer  = ownOffice && await _permissions.CanReviewBudgetPlanningAsync(caller, ct);
        bool open      = record.Status == PlanningStatus.Draft
                         && AipWorkflowStatus.IsOfficeEditable(officeStatus);

        HashSet<int> withWork = tree.Activities.Where(a => a.DivisionId is not null)
            .Select(a => a.DivisionId!.Value).ToHashSet();

        List<AipDivisionStatusDto> rows = [];
        foreach (Division d in state.Divisions.Values
                     .Where(d => d.IsActive || withWork.Contains(d.Id))
                     .OrderBy(d => d.Id))
        {
            List<AipActivity> own = tree.Activities.Where(a => a.DivisionId == d.Id).ToList();
            bool submitted = state.IsSubmitted(d.Id);
            AipDivisionSubmission? row = state.RowFor(d.Id);

            List<string> blockers = DivisionBlockers(state, d.Id, own, untagged, tree);
            bool member = ownOffice && d.IsActive && caller.DivisionId == d.Id;

            rows.Add(new AipDivisionStatusDto(
                d.Id, d.Code, d.Name, d.IsActive,
                submitted ? AipDivisionStatus.Submitted : AipDivisionStatus.Draft,
                own.Count,
                row?.SubmittedAt,
                row?.SubmittedById is Guid by ? names.GetValueOrDefault(by) : null,
                row?.ReturnedAt,
                CanSubmit: (member || head) && open && !submitted && blockers.Count == 0,
                CanReturn: reviewer && open && submitted,
                Blockers:  submitted ? [] : blockers));
        }

        return ServiceResult<AipDivisionStatusListDto>.Ok(
            new AipDivisionStatusListDto(officeId, officeStatus, true, untagged, rows));
    }

    public async Task<ServiceResult<AipDivisionSubmitResultDto>> SubmitDivisionAsync(
        int aipRecordId, int divisionId, User caller, CancellationToken ct = default)
    {
        (DivisionContext? dc, ServiceResult<AipDivisionSubmitResultDto>? refused) =
            await ResolveDivisionAsync(aipRecordId, divisionId, caller, ct);
        if (refused is not null) return refused;
        DivisionContext c = dc!;
        string name = c.State.NameOf(divisionId);

        // Who: the division's own encoder, or the department head / Admin on its behalf (§3.2).
        bool member = c.Division.IsActive && caller.DivisionId == divisionId;
        if (!member && !c.IsDepartmentHead)
            return ServiceResult<AipDivisionSubmitResultDto>.Forbidden("You can only submit your own division.");

        if (c.State.IsSubmitted(divisionId))
            return ServiceResult<AipDivisionSubmitResultDto>.BadRequest($"{name} has already been submitted.");

        OfficeTree tree = await LoadTreeAsync(c.Groups, ct);
        List<AipActivity> own = tree.Activities.Where(a => a.DivisionId == divisionId).ToList();
        int untagged = tree.Activities.Count(a => a.DivisionId is null);

        // Empty and untagged first, then completeness — the order DivisionBlockers lists them in.
        if (own.Count == 0)
            return ServiceResult<AipDivisionSubmitResultDto>.BadRequest($"{name} has no activities to submit.");
        if (untagged > 0)
            return ServiceResult<AipDivisionSubmitResultDto>.BadRequest(UntaggedMessage(untagged));

        // ⚠️ Completeness over THIS division's activities only — another division's gaps are not
        // this division's to fix, and must not hold its submit (§3.2 "Failure: incomplete").
        List<AipReadinessIssueDto> issues = CollectIssues(own, tree.Counts);
        if (issues.Count > 0)
            return ServiceResult<AipDivisionSubmitResultDto>.BadRequest(
                FormatLines(issues.Select(i => i.Message).ToList()));

        // Decision 13 — office-wide, a warning here, a block only at the send to PPDO.
        string? ceilingWarning = await _ceiling.ValidateForSubmitAsync(c.Groups[0].Id, ct);

        DateTime now = DateTime.UtcNow;
        AipDivisionSubmission? row = c.State.RowFor(divisionId);
        if (row is null)
        {
            row = new AipDivisionSubmission
            {
                AipRecordId = aipRecordId, OfficeId = c.OfficeId, DivisionId = divisionId,
            };
            await _divRepo.AddAsync(row, ct);
        }
        row.Status        = AipDivisionStatus.Submitted;
        row.SubmittedAt   = now;
        row.SubmittedById = caller.Id;

        // Decision 9 — the office follows its divisions. When every division with activities has
        // now submitted, it moves to DepartmentReview by itself. From Draft that is the first
        // round; from ReturnedByPpdo it is the resubmit after PPDO sent it back (§3.4), which is
        // why the office stays ReturnedByPpdo until this point rather than dropping to Draft.
        //
        // ⚠️ An office already in DepartmentReview with divisions still in Draft is a legacy state
        // (a UAT office submitted before this flow existed, spec §5). It is left where it is.
        string officeFrom = c.Groups[0].WorkflowStatus;
        bool allIn = c.State.RequiredDivisionIds(tree.Activities)
            .All(id => id == divisionId || c.State.IsSubmitted(id));
        bool officeMoves = allIn && officeFrom is AipWorkflowStatus.Draft or AipWorkflowStatus.ReturnedByPpdo;
        if (officeMoves)
            foreach (AipOffice group in c.Groups)
            {
                group.WorkflowStatus = AipWorkflowStatus.DepartmentReview;
                await _officeRepo.UpdateAsync(group, ct);
            }

        try
        {
            // One save: the division row and the office rows share the DbContext.
            await _divRepo.SaveChangesAsync(ct);
        }
        catch (UniqueConstraintViolationException)
        {
            // Two people pressed Submit on the same division at once, and the other one's first
            // row landed first (§3.2 "Concurrency"). Their submit stands; this one is a duplicate.
            return ServiceResult<AipDivisionSubmitResultDto>.Conflict(
                $"{name} was submitted by someone else a moment ago. Reload to see the current state.");
        }

        await _audit.LogAsync("aip_division_submissions", row.Id, AuditAction.SubmitDivision,
            new { Status = AipDivisionStatus.Draft },
            new { Status = AipDivisionStatus.Submitted, row.AipRecordId, row.OfficeId, row.DivisionId }, ct);
        _logger.LogInformation(
            "AIP division submitted. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "DivisionId: {DivisionId}, UserId: {UserId}",
            aipRecordId, c.OfficeId, divisionId, caller.Id);

        if (officeMoves)
        {
            // The office's own hand-off row, the shape History reads (PPDO-77) — the division rows
            // are deliberately not in AuditAction.AipHandOffs (see there).
            await _audit.LogAsync("aip_offices", c.Groups[0].Id, AuditAction.SubmitToDeptHead,
                new { WorkflowStatus = officeFrom },
                new
                {
                    WorkflowStatus = AipWorkflowStatus.DepartmentReview,
                    GroupIds       = c.Groups.Select(g => g.Id).ToArray(),
                    LastDivisionId = divisionId,
                }, ct);
            _logger.LogInformation(
                "AIP office moved to department review by its last division. AipRecordId: {AipRecordId}, "
                + "OfficeId: {OfficeId}, From: {From}, DivisionId: {DivisionId}",
                aipRecordId, c.OfficeId, officeFrom, divisionId);
        }

        if (ceilingWarning is not null)
            _logger.LogWarning(
                "AIP division submitted over the office ceiling. AipRecordId: {AipRecordId}, "
                + "OfficeId: {OfficeId}, DivisionId: {DivisionId}, UserId: {UserId}",
                aipRecordId, c.OfficeId, divisionId, caller.Id);

        return ServiceResult<AipDivisionSubmitResultDto>.Ok(new AipDivisionSubmitResultDto(
            divisionId, AipDivisionStatus.Submitted, c.Groups[0].WorkflowStatus, ceilingWarning));
    }

    public async Task<ServiceResult<AipDivisionSubmitResultDto>> ReturnDivisionAsync(
        int aipRecordId, int divisionId, User caller, CancellationToken ct = default)
    {
        (DivisionContext? dc, ServiceResult<AipDivisionSubmitResultDto>? refused) =
            await ResolveDivisionAsync(aipRecordId, divisionId, caller, ct);
        if (refused is not null) return refused;
        DivisionContext c = dc!;

        // ⚠️ The review grant itself, not IsDepartmentHead: a plain Admin may submit on a division's
        // behalf but not return one, matching the office-level return (see AipSubmitFunctions).
        if (!await _permissions.CanReviewBudgetPlanningAsync(caller, ct))
            return ServiceResult<AipDivisionSubmitResultDto>.Forbidden(
                "Only the department head can return a division.");

        if (!c.State.IsSubmitted(divisionId))
            return ServiceResult<AipDivisionSubmitResultDto>.BadRequest(
                $"{c.State.NameOf(divisionId)} is still with its encoders.");

        AipDivisionSubmission row = c.State.RowFor(divisionId)!;
        row.Status       = AipDivisionStatus.Draft;
        row.ReturnedAt   = DateTime.UtcNow;
        row.ReturnedById = caller.Id;

        // Decision 3 / §3.4 — not every division is submitted any more, so an office in department
        // review drops back to Draft. From ReturnedByPpdo it stays put: the "returned by PPDO"
        // banner must stay up until the office goes back to review.
        string officeFrom  = c.Groups[0].WorkflowStatus;
        bool   officeMoves = officeFrom == AipWorkflowStatus.DepartmentReview;
        if (officeMoves)
            foreach (AipOffice group in c.Groups)
            {
                group.WorkflowStatus = AipWorkflowStatus.Draft;
                await _officeRepo.UpdateAsync(group, ct);
            }

        await _divRepo.SaveChangesAsync(ct);

        await _divisions.AuditReturnsAsync([row], "department-head", ct);
        if (officeMoves)
            await _audit.LogAsync("aip_offices", c.Groups[0].Id, AuditAction.ReturnToEncoder,
                new { WorkflowStatus = AipWorkflowStatus.DepartmentReview },
                new
                {
                    WorkflowStatus = AipWorkflowStatus.Draft,
                    GroupIds       = c.Groups.Select(g => g.Id).ToArray(),
                    DivisionId     = divisionId,
                }, ct);

        return ServiceResult<AipDivisionSubmitResultDto>.Ok(new AipDivisionSubmitResultDto(
            divisionId, AipDivisionStatus.Draft, c.Groups[0].WorkflowStatus));
    }

    /// <summary>
    /// The shared front half of a division submit or return: the division, its office's groups in
    /// this record, the caller's standing, and the refusals common to both (§3.2, §3.4).
    /// </summary>
    private async Task<(DivisionContext?, ServiceResult<AipDivisionSubmitResultDto>?)> ResolveDivisionAsync(
        int aipRecordId, int divisionId, User caller, CancellationToken ct)
    {
        AipRecord? record = await _aipRepo.GetByIntIdAsync(aipRecordId, ct);
        if (record is null)
            return (null, ServiceResult<AipDivisionSubmitResultDto>.NotFound($"AIP record {aipRecordId} not found."));

        // ⚠️ One sentence for "no such division" and "another office's division" (PPDO-46). The
        // office acted on is the caller's OWN — the same rule as the office-level hand-offs, so a
        // PPDO Admin cannot move a guest office's division any more than its office.
        string notFound = $"Division {divisionId} not found in record {aipRecordId}.";
        Division? division = await _divRepo.GetDivisionAsync(divisionId, ct);
        if (division is null || caller.OfficeId != division.OfficeId)
            return (null, ServiceResult<AipDivisionSubmitResultDto>.NotFound(notFound));

        List<AipOffice> groups = (await _aipRepo.GetOfficesByAipIdAsync(aipRecordId, ct))
            .Where(o => o.OfficeId == division.OfficeId).ToList();
        if (groups.Count == 0)
            return (null, ServiceResult<AipDivisionSubmitResultDto>.NotFound(notFound));

        if (record.Status != PlanningStatus.Draft)
            return (null, ServiceResult<AipDivisionSubmitResultDto>.BadRequest(
                $"The FY {record.FiscalYear} AIP is '{record.Status}' and cannot be submitted."));

        AipDivisionOfficeState? state = await _divisions.LoadAsync(record, division.OfficeId, ct);
        if (state is null)
            return (null, ServiceResult<AipDivisionSubmitResultDto>.BadRequest(
                "This office has no divisions — submit the whole office instead."));

        if (!AipWorkflowStatus.IsOfficeEditable(groups[0].WorkflowStatus))
            return (null, ServiceResult<AipDivisionSubmitResultDto>.BadRequest(
                $"This office's AIP is in {AipWriteGuard.Describe(groups[0].WorkflowStatus)}. "
                + "Its divisions can no longer be submitted or returned."));

        return (new DivisionContext(record, division, division.OfficeId, groups, state,
            await IsDepartmentHeadAsync(caller, ct)), null);
    }

    /// <summary>
    /// The department head for the caller's own office: Admin/SuperAdmin, or the
    /// <c>CanReviewBudgetPlanning</c> holder. ⚠️ Only ever asked about the caller's own office —
    /// the flag has no office in it (see AipSubmitFunctions.CanReview).
    /// </summary>
    private async Task<bool> IsDepartmentHeadAsync(User caller, CancellationToken ct)
        => caller.Role is UserRole.SuperAdmin or UserRole.Admin
           || await _permissions.CanReviewBudgetPlanningAsync(caller, ct);

    /// <summary>Why a division cannot be submitted, in the order the submit refuses.</summary>
    private static List<string> DivisionBlockers(
        AipDivisionOfficeState state, int divisionId, List<AipActivity> own, int untagged, OfficeTree tree)
    {
        List<string> blockers = [];
        if (own.Count == 0) blockers.Add($"{state.NameOf(divisionId)} has no activities to submit.");
        if (untagged > 0) blockers.Add(UntaggedMessage(untagged));
        blockers.AddRange(CollectIssues(own, tree.Counts).Select(i => i.Message));
        return blockers;
    }

    private static string UntaggedMessage(int count)
        => count == 1
            ? "1 activity in this office has no division. Your department head must assign it before any division can submit."
            : $"{count} activities in this office have no division. Your department head must assign them before any division can submit.";

    private sealed record DivisionContext(
        AipRecord Record, Division Division, int OfficeId, List<AipOffice> Groups,
        AipDivisionOfficeState State, bool IsDepartmentHead);

    // ── The checklist ─────────────────────────────────────────────────────────

    private sealed record OfficeTree(
        IReadOnlyList<AipActivity> Activities,
        IReadOnlyDictionary<int, AipActivityLineCountDto> Counts);

    /// <summary>
    /// Every activity across the office's groups and their line counts. Four queries regardless of
    /// size — programs, projects, activities, line counts — never one per node.
    /// </summary>
    private async Task<OfficeTree> LoadTreeAsync(List<AipOffice> groups, CancellationToken ct)
    {
        List<int> groupIds = groups.Select(g => g.Id).ToList();

        IReadOnlyList<AipProgram> programs =
            await _aipRepo.GetProgramsByOfficeIdsAsync(groupIds, ct);
        IReadOnlyList<AipProject> projects =
            await _aipRepo.GetProjectsByProgramIdsAsync(programs.Select(p => p.Id).ToList(), ct);
        IReadOnlyList<AipActivity> activities =
            await _aipRepo.GetActivitiesByProjectIdsAsync(projects.Select(p => p.Id).ToList(), ct);

        IReadOnlyList<AipActivityLineCountDto> counts =
            await _expRepo.CountByActivityIdsAsync(activities.Select(a => a.Id).ToList(), ct);

        return new OfficeTree(activities, counts.ToDictionary(c => c.ActivityId));
    }

    /// <summary>
    /// The per-activity completeness checks. Shared by the office checklist and the division submit,
    /// which runs it over one division's activities (PPDO-149) — one set of rules, two scopes.
    /// </summary>
    private static List<AipReadinessIssueDto> CollectIssues(
        IEnumerable<AipActivity> activities, IReadOnlyDictionary<int, AipActivityLineCountDto> countsByActivity)
    {
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

            // ⚠️ **CC typology is NOT checked here, and must not be re-added** (PPDO-81). It used
            // to be — `missing-cc-typology` — on the assumption that every column the AIP form
            // prints is required. It is not: column (14) is filled only for an activity that
            // actually carries a climate-change component, and most do not. Blocking submit on it
            // forced encoders to invent a code for every ordinary operating activity, which is
            // worse than a blank cell — it puts fiction in a column the province reports on.
            //
            // ↩️ eSRE above stays required. The two were introduced together and read as a pair,
            // but they are not one: eSRE classifies EVERY activity, so a blank there is genuinely
            // incomplete.
        }

        return issues;
    }

    /// <param name="UntaggedCount">Untagged activities in a divisioned office; 0 outside the flow.</param>
    private sealed record Checklist(AipReadinessDto Readiness, int UntaggedCount);

    /// <summary>Runs every check across the office's whole subtree.</summary>
    private async Task<Checklist> BuildAsync(ReadinessContext ctx, CancellationToken ct)
    {
        OfficeTree tree = await LoadTreeAsync(ctx.Groups, ct);
        List<AipReadinessIssueDto> issues = CollectIssues(tree.Activities, tree.Counts);

        // ⚠️ An office with nothing in it is not "ready" — it is empty. Submitting it would hand a
        // department head a blank document with no indication anything was wrong.
        if (tree.Activities.Count == 0)
            issues.Add(new AipReadinessIssueDto(
                "empty", null, null,
                "There is nothing to submit yet — this office has no activities."));

        // The ceiling is checked once for the office, not per group: the bound is the office's own
        // General Fund ceiling (V18-46).
        //
        // ↩️ PPDO-146 (Division_Submit_Spec.md decision 13): a WARNING, not an issue. Over the
        // ceiling no longer blocks the submit to the department head; it blocks the send to PPDO.
        // Kept out of `issues` so CanSubmit means "complete" and nothing else.
        AipCeilingStatusDto? ceiling = null;
        string? ceilingWarning = null;
        AipDivisionOfficeState? divisions = null;
        if (ctx.Groups.Count > 0)
        {
            ceiling = await _ceiling.GetStatusAsync(ctx.Groups[0].Id, ct);
            ceilingWarning = await _ceiling.ValidateForSubmitAsync(ctx.Groups[0].Id, ct);
            divisions = await _divisions.LoadAsync(ctx.Record, ctx.OfficeId, ct);
        }

        bool complete = issues.Count == 0;

        // ↩️ PPDO-149. In an office with divisions the office-level submit is closed, and the send to
        // PPDO also waits for every division with activities and for every activity to be tagged.
        // Neither is an `issue`: they are not about the work's content, and adding them there would
        // make every division's blocker list carry the others' state.
        IReadOnlyList<string> waiting = divisions is null ? [] : divisions.WaitingNames(tree.Activities);
        bool anyUntagged = divisions is not null && tree.Activities.Any(a => a.DivisionId is null);

        AipReadinessDto readiness = new(
            ctx.Record.Id, ctx.OfficeId,
            ctx.Groups.Count > 0 ? ctx.Groups[0].WorkflowStatus : AipWorkflowStatus.Draft,
            CanSubmit: complete && divisions is null,
            ActivityCount: tree.Activities.Count,
            Issues: issues,
            Ceiling: ceiling,
            CeilingWarning: ceilingWarning,
            CanSubmitToPpdo: complete && ceilingWarning is null && waiting.Count == 0 && !anyUntagged,
            SubmitsByDivision: divisions is not null,
            WaitingDivisions: waiting);

        return new Checklist(readiness, anyUntagged ? tree.Activities.Count(a => a.DivisionId is null) : 0);
    }

    /// <summary>
    /// The refusal body. ⚠️ A <b>list</b>, one entry per failing node, never a single sentence
    /// (spec §4 error shapes) — and capped, because an office that has costed nothing would
    /// otherwise produce hundreds of lines nobody reads.
    /// </summary>
    /// <param name="includeCeiling">
    /// True for the send to PPDO, where the ceiling blocks (PPDO-146). The ceiling line goes
    /// <b>first</b> so the cap on listed issues can never hide it.
    /// </param>
    /// <param name="untaggedCount">
    /// The send to PPDO only (PPDO-149). With <paramref name="includeCeiling"/>, the divisions still
    /// waiting and the untagged count go ahead of everything — they are what the department head has
    /// to chase.
    /// </param>
    private static string FormatRefusal(
        AipReadinessDto readiness, bool includeCeiling = false, int untaggedCount = 0)
    {
        List<string> messages = [];
        if (readiness.WaitingDivisions is { Count: > 0 } waiting)
            messages.Add(WaitingMessage(waiting));
        if (untaggedCount > 0)
            messages.Add(UntaggedMessage(untaggedCount));
        if (includeCeiling && readiness.CeilingWarning is string ceilingWarning)
            messages.Add(ceilingWarning);
        messages.AddRange(readiness.Issues.Select(i => i.Message));
        return FormatLines(messages);
    }

    /// <summary>"Engineering Division has not submitted yet." — every waiting division named (§3.4).</summary>
    private static string WaitingMessage(IReadOnlyList<string> names)
        => names.Count == 1
            ? $"{names[0]} has not submitted yet."
            : $"{string.Join(", ", names.Take(names.Count - 1))} and {names[^1]} have not submitted yet.";

    private static string FormatLines(IReadOnlyList<string> messages)
    {
        const int shown = 10;
        IEnumerable<string> lines = messages.Take(shown).Select(m => "• " + m);
        string body = string.Join("\n", lines);

        int remaining = messages.Count - shown;
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
