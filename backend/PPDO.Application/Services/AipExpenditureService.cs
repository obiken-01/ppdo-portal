using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
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
    private readonly ILogger<AipExpenditureService> _logger;

    public AipExpenditureService(
        IAipRepository             aipRepo,
        IAipExpenditureRepository  expRepo,
        IAipActivityTotalsService  totals,
        IAipCeilingService         ceiling,
        IRepository<Account>       accountRepo,
        IRepository<FundingSource> fsRepo,
        IAuditService              audit,
        ILogger<AipExpenditureService> logger)
    {
        _aipRepo     = aipRepo;
        _expRepo     = expRepo;
        _totals      = totals;
        _ceiling     = ceiling;
        _accountRepo = accountRepo;
        _fsRepo      = fsRepo;
        _audit       = audit;
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
        if (!OfficeScope.Resolve(caller).Permits(ctx.Office.OfficeId))
            return ServiceResult<IReadOnlyList<AipExpenditureDto>>.NotFound(NotFound(activityId));

        IReadOnlyList<AipExpenditure> lines = await _expRepo.GetByActivityIdAsync(activityId, ct);
        return ServiceResult<IReadOnlyList<AipExpenditureDto>>.Ok(lines.Select(Map).ToList());
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

        AipExpenditure line = new()
        {
            ActivityId = activityId,
            Ps   = dto.Ps,
            Mooe = dto.Mooe,
            Co   = dto.Co,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        await ApplySnapshotsAsync(line, dto.AccountId, dto.FundingSourceId, ct);
        line.Recalculate();

        await _expRepo.AddAsync(line, ct);
        await _expRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("aip_expenditures", line.Id, AuditAction.Create,
            null, new { line.ActivityId, line.Ps, line.Mooe, line.Co, line.Total }, ct);
        _logger.LogInformation(
            "AIP expenditure added. ActivityId: {ActivityId}, ExpenditureId: {ExpenditureId}, Total: {Total}",
            activityId, line.Id, line.Total);

        return await AfterWriteAsync(line, activityId, afterDelete: false, ct);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipExpenditureWriteResultDto>> UpdateAsync(
        int expenditureId, UpdateAipExpenditureDto dto, User caller, CancellationToken ct = default)
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

        object before = new { line.AccountId, line.FundingSourceId, line.Ps, line.Mooe, line.Co, line.Total };

        line.Ps   = dto.Ps;
        line.Mooe = dto.Mooe;
        line.Co   = dto.Co;
        line.UpdatedAt = DateTime.UtcNow;
        await ApplySnapshotsAsync(line, dto.AccountId, dto.FundingSourceId, ct);
        line.Recalculate();

        await _expRepo.UpdateAsync(line, ct);
        await _expRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("aip_expenditures", line.Id, AuditAction.Update, before,
            new { line.AccountId, line.FundingSourceId, line.Ps, line.Mooe, line.Co, line.Total }, ct);

        return await AfterWriteAsync(line, line.ActivityId, afterDelete: false, ct);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<AipExpenditureWriteResultDto>> DeleteAsync(
        int expenditureId, User caller, CancellationToken ct = default)
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

        await _audit.LogAsync("aip_expenditures", line.Id, AuditAction.Delete,
            new { line.ActivityId, line.Ps, line.Mooe, line.Co, line.Total }, null, ct);

        await _expRepo.DeleteAsync(line, ct);
        await _expRepo.SaveChangesAsync(ct);

        // ⚠️ afterDelete: true. This is the one call site where removing the last line must take
        // the activity to 0 rather than leaving it untouched — see IAipExpenditureService.
        return await AfterWriteAsync(line: null, activityId, afterDelete: true, ct);
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

        return ServiceResult<AipExpenditureWriteResultDto>.Ok(new AipExpenditureWriteResultDto(
            line is null ? null : Map(line),
            activityId,
            activity?.Ps, activity?.Mooe, activity?.Co, activity?.Total,
            totals.LineCount));
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
    ///
    /// ⚠️ Both halves, always. The FK answers "which config row is this?"; the snapshot answers
    /// "what did it say when this was entered?" — and the second is the one an auditor asks after
    /// somebody renames an account.
    /// </summary>
    private async Task ApplySnapshotsAsync(
        AipExpenditure line, int? accountId, int? fundingSourceId, CancellationToken ct)
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

        line.FundingSourceId = fundingSourceId;
        if (fundingSourceId is int fid)
        {
            FundingSource? fund = (await _fsRepo.GetAllAsync(ct)).FirstOrDefault(f => f.Id == fid);
            line.FundingSourceSnapshot     = fund?.Code;
            line.FundingSourceNameSnapshot = fund?.Name;
        }
        else
        {
            line.FundingSourceSnapshot     = null;
            line.FundingSourceNameSnapshot = null;
        }
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

    private static string NotFound(int activityId) => $"AIP activity {activityId} not found.";
    private static string NotFoundLine(int id)     => $"AIP expenditure {id} not found.";

    private static AipExpenditureDto Map(AipExpenditure e) => new(
        e.Id, e.ActivityId,
        e.AccountId, e.AccountNumberSnapshot, e.AccountTitleSnapshot,
        e.FundingSourceId, e.FundingSourceSnapshot, e.FundingSourceNameSnapshot,
        e.Ps, e.Mooe, e.Co, e.Total);

    private sealed record AipContext(AipActivity Activity, AipOffice Office);
}
