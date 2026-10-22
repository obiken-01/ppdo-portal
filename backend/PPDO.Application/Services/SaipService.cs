using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Implementation of <see cref="ISaipService"/> (v1.10.0 — PPDO-246).
///
/// <para>
/// <b>Start</b> copies the office's approved tree into a draft copy (<see cref="SaipTreeCopier"/>)
/// and <b>cancel</b> deletes it. The approved AIP is never written by either. Both run in one
/// transaction, so a failure part-way leaves neither an SAIP without a copy nor a copy without an
/// SAIP.
/// </para>
/// </summary>
public sealed class SaipService : ISaipService
{
    private readonly ISaipRepository           _saipRepo;
    private readonly IAipRepository            _aipRepo;
    private readonly IAipExpenditureRepository _expRepo;
    private readonly IPermissionService        _permissions;
    private readonly IAuditService             _audit;
    private readonly IUserRepository           _userRepo;
    private readonly ILogger<SaipService>      _logger;

    public SaipService(
        ISaipRepository           saipRepo,
        IAipRepository            aipRepo,
        IAipExpenditureRepository expRepo,
        IPermissionService        permissions,
        IAuditService             audit,
        IUserRepository           userRepo,
        ILogger<SaipService>      logger)
    {
        _saipRepo    = saipRepo;
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
        _permissions = permissions;
        _audit       = audit;
        _userRepo    = userRepo;
        _logger      = logger;
    }

    // ── Start ─────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<SaipDto>> StartAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        (OfficeContext? found, string notFound) = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (found is null) return ServiceResult<SaipDto>.NotFound(notFound);
        OfficeContext c = found;

        // ⚠️ Before the head check: FY ≤ 2027 keeps the old amendment flow for everyone (spec §3).
        if (!AipFiscalYears.IsEntered(c.Record.FiscalYear))
            return ServiceResult<SaipDto>.BadRequest(
                $"SAIPs start from FY {AipFiscalYears.FirstEnteredFiscalYear}. FY {c.Record.FiscalYear} "
                + "uses the amendment flow.");

        if (!await IsDepartmentHeadAsync(caller, officeId, ct))
            return Denied<SaipDto>(caller, "start");

        if (c.Record.Status == PlanningStatus.Archived)
            return ServiceResult<SaipDto>.Conflict(
                $"The FY {c.Record.FiscalYear} AIP is archived. An SAIP cannot start on it.");

        // ⚠️ EVERY group row, not Groups[0]. They move together, but if they ever disagree the
        // office is not "marked as done", and a copy of a half-accepted office is not an SAIP.
        if (c.Groups.Any(g => g.WorkflowStatus != AipWorkflowStatus.Consolidated))
            return ServiceResult<SaipDto>.Conflict("An SAIP starts once your AIP is marked as done.");

        AipSaip? open = await _saipRepo.GetOpenAsync(aipRecordId, officeId, ct);
        if (open is not null)
            return ServiceResult<SaipDto>.Conflict($"SAIP {open.Number} is still open.");

        int number = await _saipRepo.GetMaxNumberAsync(aipRecordId, officeId, ct) + 1;

        AipSaip saip = new()
        {
            AipRecordId = aipRecordId,
            OfficeId    = officeId,
            Number      = number,
            Status      = SaipStatus.Draft,
            StartedById = caller.Id,
            StartedAt   = DateTime.UtcNow,
        };

        SaipTreeCopier.DraftCopy? copy = null;
        try
        {
            await _saipRepo.ExecuteInTransactionAsync(async () =>
            {
                // The SAIP row first: the copy's group rows carry its id.
                await _saipRepo.AddAsync(saip, ct);
                await _saipRepo.SaveChangesAsync(ct);

                copy = await BuildCopyAsync(saip.Id, c.Groups, ct);
                await _saipRepo.AddDraftCopyAsync(copy.Groups, copy.Expenditures, ct);
                await _saipRepo.SaveChangesAsync(ct);
            }, ct);
        }
        catch (UniqueConstraintViolationException ex)
        {
            // Two starts raced past the open check; the filtered unique index refused the second.
            _logger.LogWarning(ex,
                "SAIP start refused by the one-open index. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, UserId: {UserId}",
                aipRecordId, officeId, caller.Id);
            AipSaip? winner = await _saipRepo.GetOpenAsync(aipRecordId, officeId, ct);
            return ServiceResult<SaipDto>.Conflict(
                winner is null ? "An SAIP was started at the same time. Reload and try again."
                               : $"SAIP {winner.Number} is still open.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SAIP start failed; nothing was saved. AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, UserId: {UserId}",
                aipRecordId, officeId, caller.Id);
            throw;
        }

        int activityCount = copy!.Groups
            .SelectMany(g => g.Programs).SelectMany(p => p.Projects).Sum(j => j.Activities.Count);

        await _audit.LogAsync("aip_saips", saip.Id, AuditAction.SaipStart, null, new
        {
            saip.AipRecordId, saip.OfficeId, saip.Number, saip.Status,
            SourceGroupIds = c.Groups.Select(g => g.Id).ToArray(),
            DraftGroupIds  = copy.Groups.Select(g => g.Id).ToArray(),
        }, ct);

        _logger.LogInformation(
            "SAIP started. SaipId: {SaipId}, Number: {Number}, AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "Groups: {GroupCount}, Activities: {ActivityCount}, Lines: {LineCount}, UserId: {UserId}",
            saip.Id, saip.Number, aipRecordId, officeId, copy.Groups.Count, activityCount,
            copy.Expenditures.Count, caller.Id);

        return ServiceResult<SaipDto>.Ok(
            await ToDtoAsync(saip, c.Record, copy.Groups.Select(g => g.Id).ToList(), ct));
    }

    /// <summary>
    /// The office's approved tree, read through the ordinary (filtered) paths — so only approved
    /// rows can be copied — and copied. Five queries whatever the office's size.
    /// </summary>
    private async Task<SaipTreeCopier.DraftCopy> BuildCopyAsync(
        int saipId, IReadOnlyList<AipOffice> groups, CancellationToken ct)
    {
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(
            groups.Select(g => g.Id).ToList(), ct);
        IReadOnlyList<AipProject> projects = await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(
            programs.Select(p => p.Id).ToList(), ct);
        IReadOnlyList<AipActivity> activities = await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(
            projects.Select(j => j.Id).ToList(), ct);
        IReadOnlyList<AipExpenditure> lines = await _expRepo.GetByActivityIdsAsync(
            activities.Select(a => a.Id).ToList(), ct);
        IReadOnlyList<AipProcurementItem> items = await _expRepo.GetProcurementItemsByExpenditureIdsAsync(
            lines.Select(e => e.Id).ToList(), ct);

        return SaipTreeCopier.Copy(saipId, groups, programs, projects, activities, lines, items);
    }

    // ── Cancel ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<SaipDto>> CancelAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default)
    {
        (OfficeContext? found, string notFound) = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (found is null) return ServiceResult<SaipDto>.NotFound(notFound);

        AipSaip? saip = await _saipRepo.GetByIntIdAsync(saipId, ct);
        if (saip is null || saip.AipRecordId != aipRecordId || saip.OfficeId != officeId)
            return ServiceResult<SaipDto>.NotFound(SaipNotFound(saipId));

        if (!await IsDepartmentHeadAsync(caller, officeId, ct))
            return Denied<SaipDto>(caller, "cancel");

        if (!SaipStatus.IsOpen(saip.Status))
            return ServiceResult<SaipDto>.Conflict(
                $"SAIP {saip.Number} is already {saip.Status.ToLowerInvariant()}.");

        string before = saip.Status;
        int removed = 0;
        try
        {
            await _saipRepo.ExecuteInTransactionAsync(async () =>
            {
                removed = await _saipRepo.DeleteDraftCopyAsync(saip.Id, ct);

                saip.Status        = SaipStatus.Cancelled;
                saip.CancelledById = caller.Id;
                saip.CancelledAt   = DateTime.UtcNow;
                await _saipRepo.UpdateAsync(saip, ct);
                await _saipRepo.SaveChangesAsync(ct);
            }, ct);
        }
        catch (ConcurrencyConflictException)
        {
            // Someone else moved this SAIP between the read and the save (a second cancel, or T2/T3
            // acting on it). The transaction rolled the delete back with it.
            return ServiceResult<SaipDto>.Conflict(
                $"SAIP {saip.Number} was changed by someone else. Reload and try again.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "SAIP cancel failed; nothing was changed. SaipId: {SaipId}, UserId: {UserId}", saip.Id, caller.Id);
            throw;
        }

        await _audit.LogAsync("aip_saips", saip.Id, AuditAction.SaipCancel,
            new { Status = before },
            new { saip.Status, DraftGroupsRemoved = removed }, ct);

        _logger.LogInformation(
            "SAIP cancelled. SaipId: {SaipId}, Number: {Number}, AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "OldStatus: {OldStatus}, DraftGroupsRemoved: {DraftGroupsRemoved}, UserId: {UserId}",
            saip.Id, saip.Number, aipRecordId, officeId, before, removed, caller.Id);

        return ServiceResult<SaipDto>.Ok(await ToDtoAsync(saip, found.Record, [], ct));
    }

    // ── Reads ─────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<IReadOnlyList<SaipDto>>> ListAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct = default)
    {
        (OfficeContext? found, string notFound) = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (found is null) return ServiceResult<IReadOnlyList<SaipDto>>.NotFound(notFound);

        IReadOnlyList<AipSaip> saips = await _saipRepo.GetForOfficeAsync(aipRecordId, officeId, ct);
        IReadOnlyDictionary<Guid, string> names = await NamesAsync(saips, ct);

        List<SaipDto> rows = [];
        foreach (AipSaip s in saips)
        {
            // Only the open one has a copy — one extra query at most, not one per SAIP.
            IReadOnlyList<int> draft = SaipStatus.IsOpen(s.Status)
                ? (await _saipRepo.GetDraftOfficesAsync(s.Id, ct)).Select(o => o.Id).ToList()
                : [];
            rows.Add(ToDto(s, found.Record, draft, names));
        }

        return ServiceResult<IReadOnlyList<SaipDto>>.Ok(rows);
    }

    public async Task<ServiceResult<SaipDto>> GetAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default)
    {
        (OfficeContext? found, string notFound) = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (found is null) return ServiceResult<SaipDto>.NotFound(notFound);

        AipSaip? saip = await _saipRepo.GetByIntIdAsync(saipId, ct);
        if (saip is null || saip.AipRecordId != aipRecordId || saip.OfficeId != officeId)
            return ServiceResult<SaipDto>.NotFound(SaipNotFound(saipId));

        IReadOnlyList<int> draft = SaipStatus.IsOpen(saip.Status)
            ? (await _saipRepo.GetDraftOfficesAsync(saip.Id, ct)).Select(o => o.Id).ToList()
            : [];
        return ServiceResult<SaipDto>.Ok(await ToDtoAsync(saip, found.Record, draft, ct));
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    private sealed record OfficeContext(AipRecord Record, IReadOnlyList<AipOffice> Groups);

    /// <summary>
    /// The record and the office's approved group rows, or null when the caller may not see the
    /// office. ⚠️ Out of scope and nonexistent give the <b>same</b> 404 sentence (PPDO-46): an office
    /// the caller cannot read must be indistinguishable from one that is not there.
    /// </summary>
    private async Task<(OfficeContext? Context, string NotFound)> ResolveAsync(
        int aipRecordId, int officeId, User caller, CancellationToken ct)
    {
        string notFound = $"AIP office {officeId} not found in record {aipRecordId}.";

        AipRecord? record = await _aipRepo.GetByIntIdAsync(aipRecordId, ct);
        if (record is null) return (null, notFound);

        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        if (!OfficeScope.ResolveForReview(caller, crossOffice).Permits(officeId))
            return (null, notFound);

        // Filtered read: approved group rows only, never an SAIP's copy.
        List<AipOffice> groups = (await _aipRepo.GetOfficesByAipIdNoTrackingAsync(aipRecordId, ct))
            .Where(o => o.OfficeId == officeId)
            .ToList();
        if (groups.Count == 0) return (null, notFound);

        return (new OfficeContext(record, groups), notFound);
    }

    /// <summary>
    /// The office's department head (decision 3): Admin, or the <c>CanReviewBudgetPlanning</c>
    /// holder, of <b>this</b> office; or SuperAdmin anywhere ("as head", spec §3). ⚠️ The own-office
    /// test is what stops a host-office Admin, who can <i>read</i> every office, from starting an
    /// SAIP on another office's AIP. Same rule as <c>AipSubmitService.IsDepartmentHeadAsync</c>, plus
    /// the office the action is about.
    /// </summary>
    private async Task<bool> IsDepartmentHeadAsync(User caller, int officeId, CancellationToken ct)
    {
        if (caller.Role == UserRole.SuperAdmin) return true;
        if (caller.OfficeId != officeId) return false;
        return caller.Role == UserRole.Admin
            || await _permissions.CanReviewBudgetPlanningAsync(caller, ct);
    }

    private ServiceResult<T> Denied<T>(User caller, string action)
    {
        _logger.LogWarning(
            "Permission denied. UserId: {UserId}, Feature: {Feature}", caller.Id, "saip-" + action);
        return ServiceResult<T>.Forbidden($"Only the department head can {action} an SAIP.");
    }

    private static string SaipNotFound(int saipId) => $"SAIP {saipId} not found.";

    private async Task<SaipDto> ToDtoAsync(
        AipSaip s, AipRecord record, IReadOnlyList<int> draftIds, CancellationToken ct)
        => ToDto(s, record, draftIds, await NamesAsync([s], ct));

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(
        IReadOnlyList<AipSaip> saips, CancellationToken ct)
    {
        List<Guid> ids = saips.Select(s => s.StartedById)
            .Concat(saips.Where(s => s.CancelledById is not null).Select(s => s.CancelledById!.Value))
            .Distinct().ToList();
        return ids.Count == 0
            ? new Dictionary<Guid, string>()
            : await _userRepo.GetNamesByIdsAsync(ids, ct);
    }

    private static SaipDto ToDto(
        AipSaip s, AipRecord record, IReadOnlyList<int> draftIds, IReadOnlyDictionary<Guid, string> names)
        => new(
            s.Id, s.AipRecordId, record.FiscalYear, s.OfficeId, s.Number, s.Status,
            SaipStatus.IsOpen(s.Status),
            s.StartedAt, s.StartedById, names.GetValueOrDefault(s.StartedById),
            s.SubmittedAt, s.DecidedAt, s.CancelledAt,
            s.CancelledById is Guid by ? names.GetValueOrDefault(by) : null,
            s.LetterFileName,
            draftIds);
}
