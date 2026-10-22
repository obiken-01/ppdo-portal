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
    // PPDO-247 — the letter, the send and the live changes.
    private readonly IFileStore                       _files;
    private readonly IAipSubmitService                _submit;
    private readonly IInvestmentProposalRepository    _proposals;
    private readonly INotificationService             _notifications;
    private readonly IAipCeilingService               _ceiling;
    private readonly IAipDivisionSubmissionRepository _divisions;
    private readonly ILogger<SaipService>      _logger;

    public SaipService(
        ISaipRepository           saipRepo,
        IAipRepository            aipRepo,
        IAipExpenditureRepository expRepo,
        IPermissionService        permissions,
        IAuditService             audit,
        IUserRepository           userRepo,
        IFileStore                       files,
        IAipSubmitService                submit,
        IInvestmentProposalRepository    proposals,
        INotificationService             notifications,
        IAipCeilingService               ceiling,
        IAipDivisionSubmissionRepository divisions,
        ILogger<SaipService>      logger)
    {
        _saipRepo    = saipRepo;
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
        _permissions = permissions;
        _audit       = audit;
        _userRepo    = userRepo;
        _files         = files;
        _submit        = submit;
        _proposals     = proposals;
        _notifications = notifications;
        _ceiling       = ceiling;
        _divisions     = divisions;
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

    // ── The signed request letter (decision 7) ────────────────────────────────

    public async Task<ServiceResult<SaipDto>> PutLetterAsync(
        int aipRecordId, int officeId, int saipId, string? fileName, byte[] content, User caller,
        CancellationToken ct = default)
    {
        (OfficeContext? found, AipSaip? saip, string notFound) = await ResolveSaipAsync(aipRecordId, officeId, saipId, caller, ct);
        if (found is null || saip is null) return ServiceResult<SaipDto>.NotFound(notFound);

        if (!await IsDepartmentHeadAsync(caller, officeId, ct))
            return Denied<SaipDto>(caller, "attach the letter to");

        if (!SaipLetterRules.EditableStatuses.Contains(saip.Status))
            return ServiceResult<SaipDto>.Conflict(saip.Status == SaipStatus.SubmittedToPpdo
                ? $"The letter is locked once SAIP {saip.Number} is sent to PPDO."
                : $"SAIP {saip.Number} is {saip.Status.ToLowerInvariant()}. Its letter can no longer change.");

        if (SaipLetterRules.Validate(fileName, content, out SaipLetterRules.Kind? kind, out string name) is string bad)
            return ServiceResult<SaipDto>.BadRequest(bad);

        // A new key per upload, so a replace never overwrites the file a reader may be downloading,
        // and a failed save below leaves the old letter in place.
        string key = $"saip-letters/{saip.Id}/{Guid.NewGuid():N}{kind!.Extension}";
        string? oldKey = saip.LetterFileKey;
        object before = new { saip.LetterFileName, saip.LetterSizeBytes };

        using (MemoryStream body = new(content, writable: false))
            await _files.SaveAsync(key, body, ct);

        saip.LetterFileKey      = key;
        saip.LetterFileName     = name;
        saip.LetterContentType  = kind.ContentType;
        saip.LetterSizeBytes    = content.LongLength;
        try
        {
            await _saipRepo.UpdateAsync(saip, ct);
            await _saipRepo.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            await DeleteFileQuietlyAsync(key, saip.Id, ct);
            return ServiceResult<SaipDto>.Conflict(
                $"SAIP {saip.Number} was changed by someone else. Reload and try again.");
        }
        catch
        {
            await DeleteFileQuietlyAsync(key, saip.Id, ct);
            throw;
        }

        // The replaced file belongs to no SAIP any more. Cancel keeps the letter (decision 12);
        // replacing it is the head choosing a different one, so the old file goes.
        if (oldKey is not null) await DeleteFileQuietlyAsync(oldKey, saip.Id, ct);

        await _audit.LogAsync("aip_saips", saip.Id, AuditAction.SaipLetter, before,
            new { saip.LetterFileName, saip.LetterSizeBytes, saip.LetterContentType }, ct);
        _logger.LogInformation(
            "SAIP letter attached. SaipId: {SaipId}, Number: {Number}, OfficeId: {OfficeId}, SizeBytes: {SizeBytes}, "
            + "Replaced: {Replaced}, UserId: {UserId}",
            saip.Id, saip.Number, officeId, content.LongLength, oldKey is not null, caller.Id);

        return ServiceResult<SaipDto>.Ok(await ToDtoAsync(saip, found.Record, await DraftIdsAsync(saip, ct), ct));
    }

    public async Task<ServiceResult<SaipLetterFile>> GetLetterAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default)
    {
        (OfficeContext? found, AipSaip? saip, string notFound) = await ResolveSaipAsync(aipRecordId, officeId, saipId, caller, ct);
        if (found is null || saip is null) return ServiceResult<SaipLetterFile>.NotFound(notFound);

        if (saip.LetterFileKey is null)
            return ServiceResult<SaipLetterFile>.NotFound($"SAIP {saip.Number} has no letter yet.");

        await using Stream? stream = await _files.OpenReadAsync(saip.LetterFileKey, ct);
        if (stream is null)
        {
            _logger.LogError(
                "SAIP letter file is missing from storage. SaipId: {SaipId}, FileKey: {FileKey}", saip.Id, saip.LetterFileKey);
            return ServiceResult<SaipLetterFile>.NotFound($"SAIP {saip.Number}'s letter file could not be found.");
        }

        using MemoryStream copy = new();
        await stream.CopyToAsync(copy, ct);
        return ServiceResult<SaipLetterFile>.Ok(new SaipLetterFile(
            copy.ToArray(), saip.LetterFileName ?? "request-letter",
            saip.LetterContentType ?? "application/octet-stream"));
    }

    private async Task DeleteFileQuietlyAsync(string key, int saipId, CancellationToken ct)
    {
        try
        {
            await _files.DeleteAsync(key, ct);
        }
        catch (Exception ex)
        {
            // A stray file costs storage, not correctness: nothing points at it any more.
            _logger.LogError(ex, "SAIP letter file could not be deleted. SaipId: {SaipId}, FileKey: {FileKey}", saipId, key);
        }
    }

    // ── Send to PPDO (decisions 6–8, 10) ──────────────────────────────────────

    /// <summary>The refusal when no letter is attached (spec §3).</summary>
    public const string LetterMissingMessage = "Attach the signed request letter first.";

    private static readonly string[] SendableStates = [SaipStatus.DepartmentReview, SaipStatus.ReturnedByPpdo];

    public async Task<ServiceResult<SaipDto>> SubmitAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default)
    {
        (OfficeContext? found, AipSaip? saip, string notFound) = await ResolveSaipAsync(aipRecordId, officeId, saipId, caller, ct);
        if (found is null || saip is null) return ServiceResult<SaipDto>.NotFound(notFound);

        if (!await IsDepartmentHeadAsync(caller, officeId, ct))
            return Denied<SaipDto>(caller, "send");

        if (!SaipStatus.IsOpen(saip.Status))
            return ServiceResult<SaipDto>.Conflict($"SAIP {saip.Number} is already {saip.Status.ToLowerInvariant()}.");
        if (saip.Status == SaipStatus.SubmittedToPpdo)
            return ServiceResult<SaipDto>.Conflict($"SAIP {saip.Number} is already with PPDO.");

        SaipSendChecklist? checklist = await _submit.GetSaipChecklistAsync(aipRecordId, officeId, saip.Id, ct);
        if (checklist is null)
        {
            // An open SAIP always has a copy. Without one there is nothing to send; say so, not 500.
            _logger.LogError("Open SAIP has no draft copy. SaipId: {SaipId}, OfficeId: {OfficeId}", saip.Id, officeId);
            return ServiceResult<SaipDto>.Conflict($"SAIP {saip.Number} has no working copy. Cancel it and start again.");
        }

        // ⚠️ Every gate is checked and every failure named in one answer, the way the AIP's own send
        // does it, so a head fixing the letter does not then learn about a draft proposal.
        List<string> blockers = [];
        string state = checklist.Groups[0].WorkflowStatus;
        if (!SendableStates.Contains(state))
        {
            if (checklist.DivisionBlockers.Count > 0) blockers.AddRange(checklist.DivisionBlockers);
            else blockers.Add($"SAIP {saip.Number} has not been submitted for department review yet.");
        }
        else
        {
            blockers.AddRange(checklist.DivisionBlockers);
        }

        if (saip.LetterFileKey is null) blockers.Add(LetterMissingMessage);

        if (await UnfinalizedNewProjectsAsync(checklist.Groups, ct) is { Count: > 0 } drafts)
            blockers.Add("These new projects need a Finalized investment proposal first: "
                         + string.Join(", ", drafts) + ".");

        blockers.AddRange(checklist.WorkBlockers);
        if (checklist.CeilingBlocker is string ceiling) blockers.Add(ceiling);

        if (blockers.Count > 0)
            return ServiceResult<SaipDto>.Conflict(
                $"SAIP {saip.Number} cannot be sent to PPDO yet:\n"
                + string.Join("\n", blockers.Take(10).Select(b => "• " + b))
                + (blockers.Count > 10 ? $"\n• …and {blockers.Count - 10} more." : string.Empty));

        string from = saip.Status;
        foreach (AipOffice group in checklist.Groups)
            group.WorkflowStatus = AipWorkflowStatus.SubmittedToPpdo;  // locks the copy (AipWriteGuard)
        saip.Status        = SaipStatus.SubmittedToPpdo;
        saip.SubmittedById = caller.Id;
        saip.SubmittedAt   = DateTime.UtcNow;
        await _saipRepo.UpdateAsync(saip, ct);

        string office = caller.OfficeId == officeId && caller.Office?.OfficeCode is { Length: > 0 } code
            ? code : checklist.Groups[0].Name;
        await _notifications.StageAsync(NotificationTypes.SaipSentToPpdo, caller, async (audience, c) =>
            new NotificationBatch(
                NotificationText.SaipSentToPpdo(office, saip.Number, found.Record.FiscalYear),
                NotificationLinks.SaipReview(officeId, found.Record.FiscalYear, saip.Id),
                await audience.CrossOfficeReviewersAsync(c)), ct);

        try
        {
            // One save: the SAIP row, the copy's groups and the notifications share the DbContext.
            await _saipRepo.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return ServiceResult<SaipDto>.Conflict(
                $"SAIP {saip.Number} was changed by someone else. Reload and try again.");
        }

        await _audit.LogAsync("aip_saips", saip.Id, AuditAction.SaipSubmit,
            new { Status = from },
            new { saip.Status, DraftGroupIds = checklist.Groups.Select(g => g.Id).ToArray() }, ct);
        _logger.LogInformation(
            "SAIP sent to PPDO. SaipId: {SaipId}, Number: {Number}, AipRecordId: {AipRecordId}, OfficeId: {OfficeId}, "
            + "OldStatus: {OldStatus}, UserId: {UserId}",
            saip.Id, saip.Number, aipRecordId, officeId, from, caller.Id);

        return ServiceResult<SaipDto>.Ok(
            await ToDtoAsync(saip, found.Record, checklist.Groups.Select(g => g.Id).ToList(), ct));
    }

    /// <summary>
    /// The names of projects added in this SAIP whose investment proposal is missing or not Final
    /// (decision 8). One lookup per new project: an SAIP adds a handful, not hundreds.
    /// </summary>
    private async Task<List<string>> UnfinalizedNewProjectsAsync(IReadOnlyList<AipOffice> groups, CancellationToken ct)
    {
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(
            groups.Select(g => g.Id).ToList(), ct);
        IReadOnlyList<AipProject> projects = await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(
            programs.Select(p => p.Id).ToList(), ct);

        List<string> drafts = [];
        foreach (AipProject added in projects.Where(j => j.SourceId is null && !j.IsSynthetic))
        {
            InvestmentProposal? proposal = await _proposals.GetByProjectIdAsync(added.Id, ct);
            if (proposal?.Status != InvestmentProposalStatus.Final)
                drafts.Add($"{added.RefCode} {added.Name}".Trim());
        }
        return drafts;
    }

    // ── Changes so far (decision 17) ──────────────────────────────────────────

    public async Task<ServiceResult<SaipChangesDto>> GetChangesAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct = default)
    {
        (OfficeContext? found, AipSaip? saip, string notFound) = await ResolveSaipAsync(aipRecordId, officeId, saipId, caller, ct);
        if (found is null || saip is null) return ServiceResult<SaipChangesDto>.NotFound(notFound);

        // ↩️ Live only while open. An approved SAIP reads the saved aip_saip_changes, which approval
        // writes (PPDO-248) with this same SaipDiff; a cancelled one has nothing to show.
        if (!SaipStatus.IsOpen(saip.Status))
            return ServiceResult<SaipChangesDto>.Conflict(
                $"SAIP {saip.Number} is {saip.Status.ToLowerInvariant()}. Changes are worked out live only while an SAIP is open.");

        IReadOnlyList<AipOffice> draft = await _saipRepo.GetDraftOfficesAsync(saip.Id, ct);
        if (draft.Count == 0)
        {
            _logger.LogError("Open SAIP has no draft copy. SaipId: {SaipId}, OfficeId: {OfficeId}", saip.Id, officeId);
            return ServiceResult<SaipChangesDto>.Conflict($"SAIP {saip.Number} has no working copy. Cancel it and start again.");
        }

        // Sequential awaits — one DbContext (CLAUDE.md).
        SaipDiff.Tree approved = await LoadTreeAsync(found.Groups, ct);
        SaipDiff.Tree copy     = await LoadTreeAsync(draft, ct);
        IReadOnlyDictionary<int, string> divisionNames =
            (await _divisions.GetDivisionsByOfficeIdsAsync([officeId], ct)).ToDictionary(d => d.Id, d => d.Name);

        SaipDiffResult diff = SaipDiff.Compute(approved, copy, divisionNames);
        AipCeilingStatusDto ceilingAfter = await _ceiling.GetStatusAsync(draft[0].Id, ct);

        return ServiceResult<SaipChangesDto>.Ok(new SaipChangesDto(
            saip.Id, saip.Number, saip.Status, IsLive: true,
            diff.Changes, diff.NetByFund, diff.ApprovedTotal, diff.AmendedTotal, diff.NetChange, ceilingAfter));
    }

    /// <summary>
    /// One side of the diff: the groups' programs → projects → activities → lines, five flat queries.
    /// The by-id reads do not join <c>aip_offices</c>, so they read a copy's rows as readily as the
    /// approved ones — which side is decided by the group ids passed in.
    /// </summary>
    private async Task<SaipDiff.Tree> LoadTreeAsync(IReadOnlyList<AipOffice> groups, CancellationToken ct)
    {
        IReadOnlyList<AipProgram> programs = await _aipRepo.GetProgramsByOfficeIdsNoTrackingAsync(
            groups.Select(g => g.Id).ToList(), ct);
        IReadOnlyList<AipProject> projects = await _aipRepo.GetProjectsByProgramIdsNoTrackingAsync(
            programs.Select(p => p.Id).ToList(), ct);
        IReadOnlyList<AipActivity> activities = await _aipRepo.GetActivitiesByProjectIdsNoTrackingAsync(
            projects.Select(j => j.Id).ToList(), ct);
        IReadOnlyList<AipExpenditure> lines = await _expRepo.GetByActivityIdsAsync(
            activities.Select(a => a.Id).ToList(), ct);
        return new SaipDiff.Tree(programs, projects, activities, lines);
    }

    // ── Shared ────────────────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="ResolveAsync"/> plus the SAIP, which must belong to this office and record. Every
    /// miss is the same 404 an out-of-scope office gets (PPDO-46).
    /// </summary>
    private async Task<(OfficeContext? Context, AipSaip? Saip, string NotFound)> ResolveSaipAsync(
        int aipRecordId, int officeId, int saipId, User caller, CancellationToken ct)
    {
        (OfficeContext? found, string notFound) = await ResolveAsync(aipRecordId, officeId, caller, ct);
        if (found is null) return (null, null, notFound);

        AipSaip? saip = await _saipRepo.GetByIntIdAsync(saipId, ct);
        if (saip is null || saip.AipRecordId != aipRecordId || saip.OfficeId != officeId)
            return (null, null, SaipNotFound(saipId));

        return (found, saip, notFound);
    }

    private async Task<IReadOnlyList<int>> DraftIdsAsync(AipSaip saip, CancellationToken ct)
        => SaipStatus.IsOpen(saip.Status)
            ? (await _saipRepo.GetDraftOfficesAsync(saip.Id, ct)).Select(o => o.Id).ToList()
            : [];

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
