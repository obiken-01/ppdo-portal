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
    private readonly IPermissionService        _permissions;
    private readonly IAuditService             _audit;
    private readonly ILogger<AipReviewService> _logger;

    public AipReviewService(
        IAipRepository            aipRepo,
        IRepository<AipOffice>    officeRepo,
        IPermissionService        permissions,
        IAuditService             audit,
        ILogger<AipReviewService> logger)
    {
        _aipRepo     = aipRepo;
        _officeRepo  = officeRepo;
        _permissions = permissions;
        _audit       = audit;
        _logger      = logger;
    }

    // ── Return to the office (V18-54 / PPDO-72) ───────────────────────────────

    public async Task<ServiceResult<AipSubmitResultDto>> ReturnToOfficeAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
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
            return Refuse(blocking.WorkflowStatus);

        foreach (AipOffice group in ctx.Groups)
        {
            group.WorkflowStatus = AipWorkflowStatus.ReturnedByPpdo;
            await _officeRepo.UpdateAsync(group, ct);
        }
        await _officeRepo.SaveChangesAsync(ct);

        // ⚠️ ONE row for the transition, keyed on the first group and carrying the rest in its
        // payload — the same shape SubmitToPpdoAsync writes. An office holds several group rows
        // (decision 21), so a row-per-group would make "Show History" (PPDO-77) render one return
        // three times unless it grouped them back; matching the sibling means one read strategy
        // covers the whole chain.
        await _audit.LogAsync("aip_offices", ctx.Groups[0].Id, AuditAction.ReturnByPpdo,
            new { WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo },
            new
            {
                WorkflowStatus = AipWorkflowStatus.ReturnedByPpdo,
                GroupIds       = ctx.Groups.Select(g => g.Id).ToArray(),
            }, ct);

        _logger.LogInformation(
            "AIP returned to the office by PPDO. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "Groups: {GroupCount}, UserId: {UserId}",
            aipRecordId, officeId, ctx.Groups.Count, caller.Id);

        return ServiceResult<AipSubmitResultDto>.Ok(new AipSubmitResultDto(
            aipRecordId, officeId, AipWorkflowStatus.ReturnedByPpdo, ctx.Groups.Count));
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
    private static ServiceResult<AipSubmitResultDto> Refuse(string status) => status switch
    {
        // Verbatim from spec §4.2 — PPDO-74 asserts the same sentence for accept, so the two
        // concurrent-action refusals read alike whichever action lost the race.
        AipWorkflowStatus.ReturnedByPpdo => ServiceResult<AipSubmitResultDto>.Conflict(
            "This office was already returned by someone else. Reload to see the current state."),

        AipWorkflowStatus.Consolidated => ServiceResult<AipSubmitResultDto>.Conflict(
            $"This office was already accepted into {AipWriteGuard.Describe(AipWorkflowStatus.Consolidated)} "
            + "by someone else. Reload to see the current state."),

        // Draft and DepartmentReview, plus any state added later — closed is the safe default, and
        // "nothing to return" is true of every state that has not reached PPDO.
        _ => ServiceResult<AipSubmitResultDto>.BadRequest(
            $"This office's AIP is in {AipWriteGuard.Describe(status)} and has not been sent to "
            + "PPDO, so there is nothing to return."),
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
