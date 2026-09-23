using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Common;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>Implementation of <see cref="IAipExpenditureService"/> (V18-42 / PPDO-52).</summary>
public sealed class AipExpenditureService : IAipExpenditureService
{
    private readonly IAipRepository             _aipRepo;
    private readonly IAipExpenditureRepository  _expRepo;
    private readonly IAipActivityTotalsService  _totals;
    private readonly IAipCeilingService         _ceiling;
    private readonly IRepository<Account>       _accountRepo;
    private readonly IRepository<FundingSource> _fsRepo;
    private readonly IAuditService              _audit;
    private readonly IPermissionService         _permissions;
    private readonly IUserRepository           _userRepo;
    private readonly ILogger<AipExpenditureService> _logger;

    public AipExpenditureService(
        IAipRepository             aipRepo,
        IAipExpenditureRepository  expRepo,
        IAipActivityTotalsService  totals,
        IAipCeilingService         ceiling,
        IRepository<Account>       accountRepo,
        IRepository<FundingSource> fsRepo,
        IAuditService              audit,
        IPermissionService         permissions,
        IUserRepository            userRepo,
        ILogger<AipExpenditureService> logger)
    {
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
        _totals      = totals;
        _ceiling     = ceiling;
        _accountRepo = accountRepo;
        _fsRepo      = fsRepo;
        _audit       = audit;
        _permissions = permissions;
        _userRepo    = userRepo;
        _logger      = logger;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<IReadOnlyList<AipExpenditureDto>>> GetByActivityAsync(
        int activityId, User caller, CancellationToken ct = default)
    {
        AipContext? ctx = await ResolveAsync(activityId, ct);
        if (ctx is null)
            return ServiceResult<IReadOnlyList<AipExpenditureDto>>.NotFound(NotFound(activityId));

        // Reads clamp elsewhere; a single-node read cannot clamp, so it refuses the same way a
        // write does — with the message a missing node produces (PPDO-46).
        //
        // ⚠️ ResolveForReview, not Resolve — on THIS READ ONLY. The PPDO reviewer's one-office review
        // screen (PPDO-74) loads its lines through this endpoint, and Resolve narrows every caller
        // outside the host office to their own office. A CanReviewAllOffices holder who sits in a
        // GUEST office — a case OfficeScope.ResolveForReview's remarks name as supported — was
        // therefore refused every office but their own. It only ever worked because the reviewers
        // happened to sit in PPDO. Found while building PPDO-79.
        //
        // ⚠️ The write paths below keep Resolve, through AipWriteGuard, and must: the cross-office
        // grant is READ scope. Letting it reach a write would turn a comment-only reviewer into an
        // editor of every office. Pinned by AddAsync_ByACrossOfficeReviewer_StillCannotWriteAnotherOffice.
        bool crossOffice = await _permissions.CanReviewAllOfficesAsync(caller, ct);
        if (!OfficeScope.ResolveForReview(caller, crossOffice).Permits(ctx.Office.OfficeId))
            return ServiceResult<IReadOnlyList<AipExpenditureDto>>.NotFound(NotFound(activityId));

        IReadOnlyList<AipExpenditure> lines = await _expRepo.GetByActivityIdAsync(activityId, ct);

        // ⚠️ One query for every line's items, not one per line — see the repository method's
        // remarks. An activity with 20 lines would otherwise cost 21 round trips to render.
        ILookup<int, AipProcurementItem> itemsByLine = await LoadItemsAsync(lines, ct);

        return ServiceResult<IReadOnlyList<AipExpenditureDto>>.Ok(
            lines.Select(l => Map(l, itemsByLine[l.Id])).ToList());
    }

    // ── Add ───────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipExpenditureWriteResultDto>> AddAsync(
        int activityId, CreateAipExpenditureDto dto, User caller, CancellationToken ct = default)
    {
        AipContext? ctx = await ResolveAsync(activityId, ct);
        if (ctx is null)
            return ServiceResult<AipExpenditureWriteResultDto>.NotFound(NotFound(activityId));

        ServiceResult<AipExpenditureWriteResultDto>? refused =
            await AipWriteGuard.CheckAsync<AipExpenditureWriteResultDto>(
                ctx.Office, caller, _aipRepo, NotFound(activityId), ct);
        if (refused is not null) return refused;

        if (Validate(dto.Ps, dto.Mooe, dto.Co) is string invalid)
            return ServiceResult<AipExpenditureWriteResultDto>.BadRequest(invalid);

        ServiceResult<AipExpenditureWriteResultDto>? itemsInvalid =
            ValidateItems<AipExpenditureWriteResultDto>(dto.ProcurementItems);
        if (itemsInvalid is not null) return itemsInvalid;

        AipExpenditure line = new()
        {
            ActivityId = activityId,
            Ps   = dto.Ps,
            Mooe = dto.Mooe,
            Co   = dto.Co,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        if (await ApplySnapshotsAsync(
                line, dto.AccountId, dto.FundingSourceId, ctx.Office.OfficeId, ct) is string badFund)
            return ServiceResult<AipExpenditureWriteResultDto>.BadRequest(badFund);

        // ⚠️ Routing runs BEFORE Recalculate, and overwrites the typed amounts rather than adding
        // to them — an itemised line's cost is its items' cost, full stop.
        if (await ApplyProcurementRoutingAsync<AipExpenditureWriteResultDto>(
                line, dto.AccountId, dto.ProcurementItems, ct) is { } routingError)
            return routingError;

        line.Recalculate();

        await _expRepo.AddAsync(line, ct);
        await _expRepo.SaveChangesAsync(ct);

        // The line's id only exists after the save above, so the items are attached second — one
        // extra save, and the alternative is inserting orphans keyed on 0.
        if (BuildItems(dto.ProcurementItems) is { Count: > 0 } newItems)
        {
            await _expRepo.ReplaceProcurementItemsAsync(line.Id, newItems, ct);
            await _expRepo.SaveChangesAsync(ct);
        }

        await _audit.LogAsync("aip_expenditures", line.Id, AuditAction.Create,
            null, new { line.ActivityId, line.Ps, line.Mooe, line.Co, line.Total }, ct);
        _logger.LogInformation(
            "AIP expenditure added. ActivityId: {ActivityId}, ExpenditureId: {ExpenditureId}, Total: {Total}",
            activityId, line.Id, line.Total);

        return await AfterWriteAsync(line, activityId, afterDelete: false, ct);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipExpenditureWriteResultDto>> UpdateAsync(
        int expenditureId, UpdateAipExpenditureDto dto, User caller,
        byte[]? expectedRowVersion = null, CancellationToken ct = default)
    {
        AipExpenditure? line = await _expRepo.GetByIntIdAsync(expenditureId, ct);
        if (line is null)
            return ServiceResult<AipExpenditureWriteResultDto>.NotFound(NotFoundLine(expenditureId));

        AipContext? ctx = await ResolveAsync(line.ActivityId, ct);
        if (ctx is null)
            return ServiceResult<AipExpenditureWriteResultDto>.NotFound(NotFoundLine(expenditureId));

        ServiceResult<AipExpenditureWriteResultDto>? refused =
            await AipWriteGuard.CheckAsync<AipExpenditureWriteResultDto>(
                ctx.Office, caller, _aipRepo, NotFoundLine(expenditureId), ct, "edit");
        if (refused is not null) return refused;

        if (Validate(dto.Ps, dto.Mooe, dto.Co) is string invalid)
            return ServiceResult<AipExpenditureWriteResultDto>.BadRequest(invalid);

        ServiceResult<AipExpenditureWriteResultDto>? itemsInvalid =
            ValidateItems<AipExpenditureWriteResultDto>(dto.ProcurementItems);
        if (itemsInvalid is not null) return itemsInvalid;

        object before = new { line.AccountId, line.FundingSourceId, line.Ps, line.Mooe, line.Co, line.Total };

        line.Ps   = dto.Ps;
        line.Mooe = dto.Mooe;
        line.Co   = dto.Co;
        line.UpdatedAt = DateTime.UtcNow;
        // ⚠️ Unconditional, and PPDO-118 depends on it staying that way. This write is what dirties
        // the parent row when ONLY its procurement items changed, and the parent's rowversion is
        // what protects those items — they have no endpoints and no token of their own. Make this
        // conditional as an "optimisation" and two encoders editing items under one line both
        // succeed, silently.
        line.UpdatedById = caller.Id;
        // ⚠️ The refusal below lands AFTER these assignments, so `line` — a tracked entity — is left
        // dirty. That is safe only because this method returns without calling SaveChangesAsync and
        // the DbContext is scoped to the request, and it is pinned by
        // AipExpenditureFundScopeTests.Update_SwitchingToAnotherOfficesFund_IsRefused, which asserts
        // no save happens. Anyone adding a save between here and the return has to move this check up.
        if (await ApplySnapshotsAsync(
                line, dto.AccountId, dto.FundingSourceId, ctx.Office.OfficeId, ct) is string badFund)
            return ServiceResult<AipExpenditureWriteResultDto>.BadRequest(badFund);

        // ⚠️ null and empty differ here (see UpdateAipExpenditureDto): null leaves the existing
        // items alone, so a caller that never learned about procurement cannot silently strip them;
        // an empty list is an explicit "remove them all" and returns the line to a typed amount.
        IReadOnlyList<AipProcurementItem> existingItems = dto.ProcurementItems is null
            ? await _expRepo.GetProcurementItemsByExpenditureIdsAsync([line.Id], ct)
            : [];

        if (dto.ProcurementItems is null)
        {
            if (existingItems.Count > 0 &&
                await ApplyRoutedTotalAsync<AipExpenditureWriteResultDto>(
                    line, dto.AccountId, existingItems.Sum(i => i.LineTotal), ct) is { } keepError)
                return keepError;
        }
        else if (await ApplyProcurementRoutingAsync<AipExpenditureWriteResultDto>(
                     line, dto.AccountId, dto.ProcurementItems, ct) is { } routingError)
        {
            return routingError;
        }

        line.Recalculate();

        await _expRepo.UpdateAsync(line, ct);

        if (dto.ProcurementItems is not null)
            await _expRepo.ReplaceProcurementItemsAsync(line.Id, BuildItems(dto.ProcurementItems), ct);

        // Declared immediately before the save that matters. An intervening SaveChanges — the
        // audit service performs its own — calls AcceptAllChanges and copies current values over
        // original ones, which would silently discard the expectation.
        _expRepo.ExpectRowVersion(line, expectedRowVersion);

        try
        {
            await _expRepo.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return await LineConflictAsync(line, caller, ct);
        }

        await _audit.LogAsync("aip_expenditures", line.Id, AuditAction.Update, before,
            new { line.AccountId, line.FundingSourceId, line.Ps, line.Mooe, line.Co, line.Total }, ct);

        return await AfterWriteAsync(line, line.ActivityId, afterDelete: false, ct);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipExpenditureWriteResultDto>> DeleteAsync(
        int expenditureId, User caller,
        byte[]? expectedRowVersion = null, CancellationToken ct = default)
    {
        AipExpenditure? line = await _expRepo.GetByIntIdAsync(expenditureId, ct);
        if (line is null)
            return ServiceResult<AipExpenditureWriteResultDto>.NotFound(NotFoundLine(expenditureId));

        int activityId = line.ActivityId;

        AipContext? ctx = await ResolveAsync(activityId, ct);
        if (ctx is null)
            return ServiceResult<AipExpenditureWriteResultDto>.NotFound(NotFoundLine(expenditureId));

        ServiceResult<AipExpenditureWriteResultDto>? refused =
            await AipWriteGuard.CheckAsync<AipExpenditureWriteResultDto>(
                ctx.Office, caller, _aipRepo, NotFoundLine(expenditureId), ct, "delete from");
        if (refused is not null) return refused;

        // Snapshot BEFORE the delete, log AFTER it — the values are still readable on the
        // detached entity afterwards.
        //
        // ↩️ The audit call used to run first. That was harmless while a delete could not fail,
        // but PPDO-118 gives it a way to: a stale version now rejects the save, and an audit row
        // saying "deleted" would already have been committed for a row that still exists. The
        // audit service performs its own SaveChangesAsync, so this is not one transaction that
        // rolls back together — the ordering is the only thing keeping the two honest.
        object deletedSnapshot = new { line.ActivityId, line.Ps, line.Mooe, line.Co, line.Total };

        await _expRepo.DeleteAsync(line, ct);

        // Declared here, not earlier: the audit service's own SaveChanges would otherwise call
        // AcceptAllChanges and discard the expectation before it could be used.
        _expRepo.ExpectRowVersion(line, expectedRowVersion);

        try
        {
            await _expRepo.SaveChangesAsync(ct);
        }
        catch (ConcurrencyConflictException)
        {
            return await LineConflictAsync(line, caller, ct);
        }

        await _audit.LogAsync("aip_expenditures", expenditureId, AuditAction.Delete,
            deletedSnapshot, null, ct);

        // ⚠️ afterDelete: true. This is the one call site where removing the last line must take
        // the activity to 0 rather than leaving it untouched — see IAipExpenditureService.
        return await AfterWriteAsync(line: null, activityId, afterDelete: true, ct);
    }

    // ── Concurrent-edit guard (V18-71 / PPDO-118) ─────────────────────────────

    private const string LineNoun = "expenditure line";

    /// <summary>
    /// Builds the 409 after a rejected line save: who saved last, when, and what the line now
    /// says.
    ///
    /// <para>
    /// ⚠️ This also covers the line's <b>procurement items</b>. They have no endpoints and no
    /// token of their own — they are written only through this row, as a wholesale replace — so
    /// the parent's rowversion is what protects them. See <see cref="AipExpenditure.RowVersion"/>.
    /// </para>
    /// </summary>
    private async Task<ServiceResult<AipExpenditureWriteResultDto>> LineConflictAsync(
        AipExpenditure line, User caller, CancellationToken ct)
    {
        int lineId = line.Id;

        // Reload, not re-query: the change tracker still holds this entity with the caller's
        // rejected values, and a fresh query identity-resolves straight back to it.
        try
        {
            await _expRepo.ReloadAsync(line, ct);
        }
        catch (Exception ex)
        {
            // A conflict we cannot describe is still a conflict. A 500 here would turn "someone
            // else edited this" into "the app broke".
            _logger.LogError(ex,
                "Could not reload AIP expenditure after a concurrency conflict. ExpenditureId: {ExpenditureId}",
                lineId);
            return ServiceResult<AipExpenditureWriteResultDto>.Conflict(
                AipConflictNarration.Message(LineNoun, null, sameUser: false));
        }

        AipExpenditure? current = await _expRepo.GetByIntIdAsync(lineId, ct);
        if (current is null)
            return ServiceResult<AipExpenditureWriteResultDto>.NotFound(
                AipConflictNarration.DeletedMessage(LineNoun));

        (string? changedByName, bool sameUser) = await AipConflictNarration.ResolveEditorAsync(
            current.UpdatedById, caller.Id, _userRepo, ct);

        _logger.LogWarning(
            "AIP concurrent edit rejected. ExpenditureId: {ExpenditureId}, ActivityId: {ActivityId}, AttemptedByUserId: {AttemptedByUserId}, ChangedByUserId: {ChangedByUserId}",
            lineId, current.ActivityId, caller.Id, current.UpdatedById);

        IReadOnlyList<AipProcurementItem> items =
            await _expRepo.GetProcurementItemsByExpenditureIdsAsync([lineId], ct);

        return ServiceResult<AipExpenditureWriteResultDto>.Conflict(
            AipConflictNarration.Message(LineNoun, changedByName, sameUser),
            new AipConflictDto<AipExpenditureDto>(
                changedByName,
                current.UpdatedAt,
                Convert.ToBase64String(current.RowVersion),
                Map(current, items)));
    }

    // ── The two side effects every write owes ─────────────────────────────────

    /// <summary>
    /// Recompute, then upsert the ledger, then report the activity's new position.
    ///
    /// ⚠️ Sequential awaits, never <c>Task.WhenAll</c> — these share one <c>DbContext</c>, which is
    /// not thread-safe. Running two of them concurrently is what produced a production 500 in
    /// <c>GetStatsAsync</c> (CLAUDE.md).
    /// </summary>
    private async Task<ServiceResult<AipExpenditureWriteResultDto>> AfterWriteAsync(
        AipExpenditure? line, int activityId, bool afterDelete, CancellationToken ct)
    {
        if (afterDelete)
            await _totals.RecalculateAfterLineDeleteAsync(activityId, ct);
        else
            await _totals.RecalculateAsync(activityId, ct);

        await _ceiling.UpsertLedgerForActivityAsync(activityId, ct);

        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(activityId, ct);
        AipExpenditureTotalsDto totals = await _expRepo.SumByActivityIdAsync(activityId, ct);

        // The written line is returned with its items so the page can re-render the row without a
        // refetch — the whole reason this DTO carries the line at all.
        IReadOnlyList<AipProcurementItem> items = line is null
            ? []
            : await _expRepo.GetProcurementItemsByExpenditureIdsAsync([line.Id], ct);

        // ⚠️ Re-read after the write, not derived from the line just written. Adding a line can
        // introduce a fund, and deleting one can remove the last line naming a fund — neither is
        // visible from the written line alone, and the page renders this cell without reloading.
        IReadOnlyList<string> fundCodes = await _expRepo.GetFundCodesByActivityIdAsync(activityId, ct);

        return ServiceResult<AipExpenditureWriteResultDto>.Ok(new AipExpenditureWriteResultDto(
            line is null ? null : Map(line, items),
            activityId,
            activity?.Ps, activity?.Mooe, activity?.Co, activity?.Total,
            totals.LineCount,
            fundCodes));
    }

    // ── Internals ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Walks activity → project → program → office. Four hops rather than one join because
    /// <c>IAipRepository</c> exposes the levels separately and CLAUDE.md caps <c>Include</c> chains
    /// at two.
    /// </summary>
    private async Task<AipContext?> ResolveAsync(int activityId, CancellationToken ct)
    {
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(activityId, ct);
        if (activity is null) return null;

        AipProject? project = await _aipRepo.GetProjectByIdAsync(activity.ProjectId, ct);
        if (project is null) return null;

        AipProgram? program = await _aipRepo.GetProgramByIdAsync(project.ProgramId, ct);
        if (program is null) return null;

        AipOffice? office = await _aipRepo.GetOfficeByIdAsync(program.OfficeId, ct);
        return office is null ? null : new AipContext(activity, office);
    }

    /// <summary>
    /// Copies the account and funding-source ids <b>and</b> what those config rows say right now.
    /// Returns a validation message when the fund may not be used by this line's office, or null on
    /// success — the caller turns that into a <c>BadRequest</c>, matching <see cref="Validate"/>.
    ///
    /// ⚠️ Both halves, always. The FK answers "which config row is this?"; the snapshot answers
    /// "what did it say when this was entered?" — and the second is the one an auditor asks after
    /// somebody renames an account.
    ///
    /// ↩️ <b>The fund is now resolved against what <paramref name="owningOfficeId"/> may use</b>
    /// (follow-up to PPDO-109, via <see cref="FundingSourceScope"/>): the province-wide funds plus
    /// that office's own. Before this, any id the caller sent was accepted, so office A could
    /// snapshot office B's private fund onto its own line.
    ///
    /// ⚠️ <b>It refuses rather than clearing the field</b>, and that also fixes a second bug on
    /// the same two lines: the old code assigned <c>FundingSourceId</c> from the request BEFORE
    /// looking the row up, so an id that matched nothing at all was stored anyway, with both
    /// snapshots left null. That is a dangling FK presented as a line with no fund — worse than the
    /// refusal, because nothing downstream ever reports it.
    /// </summary>
    private async Task<string?> ApplySnapshotsAsync(
        AipExpenditure line, int? accountId, int? fundingSourceId, int? owningOfficeId,
        CancellationToken ct)
    {
        line.AccountId = accountId;
        if (accountId is int aid)
        {
            Account? account = (await _accountRepo.GetAllAsync(ct)).FirstOrDefault(a => a.Id == aid);
            line.AccountNumberSnapshot = account?.AccountNumber;
            line.AccountTitleSnapshot  = account?.AccountTitle;
        }
        else
        {
            line.AccountNumberSnapshot = null;
            line.AccountTitleSnapshot  = null;
        }

        if (fundingSourceId is int fid)
        {
            FundingSource? fund = FundingSourceScope.FindVisibleById(
                await _fsRepo.GetAllAsync(ct), fid, owningOfficeId);
            if (fund is null) return FundingSourceScope.NotFoundMessage(fid);

            line.FundingSourceId           = fid;
            line.FundingSourceSnapshot     = fund.Code;
            line.FundingSourceNameSnapshot = fund.Name;
        }
        else
        {
            line.FundingSourceId           = null;
            line.FundingSourceSnapshot     = null;
            line.FundingSourceNameSnapshot = null;
        }

        return null;
    }

    /// <summary>
    /// ⚠️ Negative amounts are refused, and a zero line is allowed. A line costed at zero is a
    /// legitimate statement ("this activity has this account, at nothing"); a negative one is not
    /// expressible on the printed form and would quietly reduce the office's ceiling consumption.
    /// </summary>
    private static string? Validate(decimal ps, decimal mooe, decimal co)
        => ps < 0m || mooe < 0m || co < 0m
            ? "PS, MOOE and CO cannot be negative."
            : null;

    // ── Procurement items (V18-80 / PPDO-54) ──────────────────────────────────

    /// <summary>
    /// ⚠️ Rejects what the arithmetic cannot express. A negative quantity or price is not a plan;
    /// a <c>numberOfDays</c> of zero silently zeroes a line the encoder just costed, which reads as
    /// the save having failed. Days default to 1 for everything that is not day-based, so zero is
    /// never the value someone meant.
    /// </summary>
    private static ServiceResult<T>? ValidateItems<T>(IReadOnlyList<SaveAipProcurementItemDto>? items)
    {
        if (items is null) return null;

        foreach (SaveAipProcurementItemDto i in items)
        {
            if (string.IsNullOrWhiteSpace(i.Name))
                return ServiceResult<T>.BadRequest("Every procurement item needs a name.");
            if (i.Qty < 0m || i.UnitPrice < 0m)
                return ServiceResult<T>.BadRequest(
                    $"'{i.Name}' has a negative quantity or unit price.");
            if (i.NumberOfDays <= 0m)
                return ServiceResult<T>.BadRequest(
                    $"'{i.Name}' has a number of days of {i.NumberOfDays}. Use 1 for items that are "
                    + "not charged per day.");
            if (i.PeriodNo is < 1 or > 4)
                return ServiceResult<T>.BadRequest(
                    $"'{i.Name}' is placed in quarter {i.PeriodNo}. Quarters run from 1 to 4.");
        }

        return null;
    }

    /// <summary>
    /// Derives the line's PS / MOOE / CO from its items, when it has any. A line with no items is
    /// left exactly as the caller typed it.
    /// </summary>
    private async Task<ServiceResult<T>?> ApplyProcurementRoutingAsync<T>(
        AipExpenditure line, int? accountId,
        IReadOnlyList<SaveAipProcurementItemDto>? items, CancellationToken ct)
    {
        if (items is null || items.Count == 0) return null;

        decimal total = items.Sum(i => i.Qty * i.UnitPrice * i.NumberOfDays);
        return await ApplyRoutedTotalAsync<T>(line, accountId, total, ct);
    }

    /// <summary>
    /// Puts <paramref name="total"/> in the one column the account's expense class names, and zeroes
    /// the other two.
    ///
    /// ⚠️ Refuses rather than defaulting when the account is missing or its expense class is
    /// unrecognised — see <see cref="AipProcurementRouting"/>. Defaulting to MOOE would put money in
    /// the wrong column of a statutory form <i>and</i> move the office's General Fund ceiling
    /// consumption, both silently.
    /// </summary>
    private async Task<ServiceResult<T>?> ApplyRoutedTotalAsync<T>(
        AipExpenditure line, int? accountId, decimal total, CancellationToken ct)
    {
        Account? account = accountId is int aid
            ? (await _accountRepo.GetAllAsync(ct)).FirstOrDefault(a => a.Id == aid)
            : null;

        if (account is null)
            return ServiceResult<T>.BadRequest(
                "An itemised line needs an account, so its total knows whether it is PS, MOOE or "
                + "Capital Outlay. Pick an account or remove the procurement items.");

        if (AipProcurementRouting.Route(account.ExpenseClass, total) is not { } routed)
            return ServiceResult<T>.BadRequest(
                $"Account {account.AccountNumber} has no recognised expense class "
                + $"('{account.ExpenseClass}'), so an itemised total cannot be placed in PS, MOOE or "
                + "Capital Outlay. Fix the account in Configuration → Accounts.");

        line.Ps   = routed.Ps;
        line.Mooe = routed.Mooe;
        line.Co   = routed.Co;
        return null;
    }

    /// <summary>
    /// Maps the save DTOs to entities, computing every <c>LineTotal</c> server-side.
    /// <see cref="AipProcurementItem.LineTotal"/> has a private setter, so this cannot be bypassed.
    /// </summary>
    private static List<AipProcurementItem> BuildItems(IReadOnlyList<SaveAipProcurementItemDto>? items)
    {
        if (items is null) return [];

        List<AipProcurementItem> built = [];
        foreach (SaveAipProcurementItemDto i in items)
        {
            AipProcurementItem item = new()
            {
                PriceIndexItemId = i.PriceIndexItemId,
                PeriodNo     = i.PeriodNo,
                Name         = i.Name.Trim(),
                Unit         = i.Unit.Trim(),
                UnitPrice    = i.UnitPrice,
                Qty          = i.Qty,
                NumberOfDays = i.NumberOfDays,
            };
            item.Recalculate();
            built.Add(item);
        }
        return built;
    }

    private async Task<ILookup<int, AipProcurementItem>> LoadItemsAsync(
        IReadOnlyList<AipExpenditure> lines, CancellationToken ct)
    {
        IReadOnlyList<AipProcurementItem> items =
            await _expRepo.GetProcurementItemsByExpenditureIdsAsync(
                lines.Select(l => l.Id).ToList(), ct);

        return items.ToLookup(i => i.ExpenditureId);
    }

    private static string NotFound(int activityId) => $"AIP activity {activityId} not found.";
    private static string NotFoundLine(int id)     => $"AIP expenditure {id} not found.";

    /// <summary>
    /// One line and its items, as the entry table renders them.
    ///
    /// ⚠️ <c>internal</c> rather than private because <see cref="AipReviewService"/>'s activity
    /// read returns the same lines (PPDO-79). Two mappers of one DTO drift the first time a
    /// column is added to it, and the review modal renders these through the very same table.
    /// </summary>
    internal static AipExpenditureDto Map(
        AipExpenditure e, IEnumerable<AipProcurementItem>? items = null) => new(
        e.Id, e.ActivityId,
        e.AccountId, e.AccountNumberSnapshot, e.AccountTitleSnapshot,
        e.FundingSourceId, e.FundingSourceSnapshot, e.FundingSourceNameSnapshot,
        e.Ps, e.Mooe, e.Co, e.Total,
        (items ?? []).Select(i => new AipProcurementItemDto(
            i.Id, i.PriceIndexItemId, i.Name, i.Unit,
            i.UnitPrice, i.Qty, i.NumberOfDays, i.LineTotal, i.PeriodNo)).ToList(),
        // The single mapper for this DTO (it is internal precisely so there is only one), so
        // every read path carries the token.
        e.RowVersion is { Length: > 0 } rv ? Convert.ToBase64String(rv) : null);

    private sealed record AipContext(AipActivity Activity, AipOffice Office);
}
