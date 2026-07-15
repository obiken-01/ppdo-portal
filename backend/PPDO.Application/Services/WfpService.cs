using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// WFP service — upsert, expenditure-line validation, snapshot population,
/// status lifecycle (RAL-64), Excel export (RAL-79), and per-division scoping (RAL-102).
///
/// SaveAsync two-pass logic:
///   0. (RAL-102) When divisionId provided: enforce setup gate, then validate Σ gross total ≤ allocation.
///   1. Validate all lines (quarterly_total ≤ net_appropriation).
///   2. Load account + funding-source dicts for snapshot population.
///   3. Delete existing activities (cascade removes lines) when updating a Draft.
///   4. Persist new activities and lines.
///
/// Reserve computation: reserveAmount = Round(totalAppropriation × 10%, 2) when ApplyReserve=true.
/// Division-budget validation uses GROSS total (D5): Σ TotalAppropriation (not net).
///
/// RAL-93: all WFP reads use IWfpRepository scoped queries instead of GetAllAsync + in-memory filter.
/// </summary>
public sealed class WfpService : IWfpService
{
    private readonly IWfpRepository               _wfpRepo;
    private readonly IRepository<WfpActivity>        _actRepo;
    private readonly IRepository<WfpExpenditureLine> _lineRepo;
    private readonly IRepository<Account>            _accountRepo;
    private readonly IRepository<FundingSource>      _fsRepo;
    private readonly IAuditService                   _audit;
    private readonly CallerContext                   _caller;
    private readonly IAipService                     _aip;
    private readonly IOfficeService                  _office;
    private readonly IWfpExcelService                _excel;
    private readonly IAllocationService              _allocation;
    private readonly IWfpCeilingService              _ceiling;
    private readonly IWfpExpenditureRepository       _expenditureRepo;

    public WfpService(
        IWfpRepository                  wfpRepo,
        IRepository<WfpActivity>        actRepo,
        IRepository<WfpExpenditureLine> lineRepo,
        IRepository<Account>            accountRepo,
        IRepository<FundingSource>      fsRepo,
        IAuditService                   audit,
        CallerContext                   caller,
        IAipService                     aip,
        IOfficeService                  office,
        IWfpExcelService                excel,
        IAllocationService              allocation,
        IWfpCeilingService              ceiling,
        IWfpExpenditureRepository       expenditureRepo)
    {
        _wfpRepo    = wfpRepo;
        _actRepo    = actRepo;
        _lineRepo   = lineRepo;
        _accountRepo = accountRepo;
        _fsRepo     = fsRepo;
        _audit      = audit;
        _caller     = caller;
        _aip        = aip;
        _office     = office;
        _excel      = excel;
        _allocation = allocation;
        _ceiling    = ceiling;
        _expenditureRepo = expenditureRepo;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WfpRecordDto>> GetAllAsync(
        int? aipRecordId, int? officeId, int? divisionId = null, CancellationToken ct = default)
        => (await _wfpRepo.GetFilteredAsync(aipRecordId, officeId, divisionId, ct)).Select(MapToDto).ToList();

    public async Task<ServiceResult<WfpRecordDetailDto>> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        WfpRecord? rec = await _wfpRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<WfpRecordDetailDto>.NotFound($"WFP record {id} not found.");

        IReadOnlyList<WfpActivity> activities = await _wfpRepo.GetActivitiesByWfpIdAsync(id, ct);
        List<int> actIds = activities.Select(a => a.Id).ToList();
        IReadOnlyList<WfpExpenditureLine> lines = await _wfpRepo.GetLinesByActivityIdsAsync(actIds, ct);

        IReadOnlyList<WfpActivityDto> actDtos = activities.Select(a =>
            new WfpActivityDto(a.Id, a.WfpId, a.AipActivityId,
                lines.Where(l => l.WfpActivityId == a.Id).Select(MapLineToDto).ToList())).ToList();

        return ServiceResult<WfpRecordDetailDto>.Ok(new WfpRecordDetailDto(
            rec.Id, rec.AipRecordId, rec.OfficeId, rec.DivisionId, rec.FiscalYear, rec.Status,
            rec.CreatedById, rec.CreatedAt, rec.UpdatedAt, rec.FinalizedAt, rec.SourceId, actDtos));
    }

    // ── Save (upsert) ─────────────────────────────────────────────────────────

    public async Task<ServiceResult<WfpRecordDto>> SaveAsync(
        SaveWfpDto dto, Guid createdById, CancellationToken ct = default)
    {
        // Find existing WFP for (aipRecordId, officeId, divisionId) — single SQL lookup.
        WfpRecord? existing = await _wfpRepo.FindByAipOfficeAndDivisionAsync(
            dto.AipRecordId, dto.OfficeId, dto.DivisionId, ct);

        if (existing is not null && existing.Status == PlanningStatus.Final)
            return ServiceResult<WfpRecordDto>.Forbidden("Cannot edit a finalized WFP.");

        // ── Pass 0 (RAL-102): division-scoped checks when divisionId is provided ──
        if (dto.DivisionId.HasValue)
        {
            // 0a. Setup gate: ceiling + allocation + program assignment must all be in place.
            AllocationSetupStatusDto setup = await _allocation.GetSetupStatusAsync(
                dto.OfficeId, dto.FiscalYear, dto.DivisionId.Value, ct);

            if (!setup.HasCeiling)
                return ServiceResult<WfpRecordDto>.BadRequest(
                    "Setup incomplete: no budget ceiling has been set for this office and fiscal year.");
            if (!setup.HasAllocation)
                return ServiceResult<WfpRecordDto>.BadRequest(
                    "Setup incomplete: no division allocation has been set for this division and fiscal year.");
            if (!setup.HasProgramAssignment)
                return ServiceResult<WfpRecordDto>.BadRequest(
                    "Setup incomplete: no programs have been assigned to this division. " +
                    "Assign programs in the Allocation page first.");

            // 0b. Division-budget validation: Σ GROSS total appropriation ≤ division allocation.
            // Legacy bulk-line grid (pre-v1.4.3) has no per-fund split in this gross-total sum,
            // so it checks against the General Fund allocation specifically (RAL-154) — matching
            // this path's pre-existing single-fund behavior. The v1.4 rework's per-expenditure
            // entry flow (WfpExpenditureService/WfpCeilingService) is the fund-scoped path.
            int? gfId = await _allocation.GetGeneralFundIdAsync(ct);
            DivisionAllocationDto? myAlloc = null;
            if (gfId is int generalFundId)
            {
                IReadOnlyList<DivisionAllocationDto> allocs =
                    await _allocation.GetAllocationsAsync(dto.OfficeId, dto.FiscalYear, generalFundId, ct);
                myAlloc = allocs.FirstOrDefault(a => a.DivisionId == dto.DivisionId.Value);
            }

            if (myAlloc is not null)
            {
                decimal grossTotal = dto.Activities
                    .SelectMany(a => a.Lines)
                    .Sum(l => l.TotalAppropriation ?? 0m);

                if (grossTotal > myAlloc.Amount)
                    return ServiceResult<WfpRecordDto>.BadRequest(
                        $"Total appropriation (₱{grossTotal:N2}) exceeds the division allocation " +
                        $"of ₱{myAlloc.Amount:N2}.");
            }
        }

        // ── Pass 1: validate all lines before any DB write ────────────────────
        List<string> errors = [];
        for (int ai = 0; ai < dto.Activities.Count; ai++)
        {
            SaveWfpActivityDto actDto = dto.Activities[ai];
            for (int li = 0; li < actDto.Lines.Count; li++)
            {
                SaveWfpExpenditureLineDto lineDto = actDto.Lines[li];
                if (!lineDto.TotalAppropriation.HasValue) continue;

                decimal reserveAmt = lineDto.ApplyReserve
                    ? Math.Round(lineDto.TotalAppropriation.Value * 0.1m, 2)
                    : 0m;
                decimal net = lineDto.TotalAppropriation.Value - reserveAmt;
                decimal quarterly = (lineDto.Q1 ?? 0m) + (lineDto.Q2 ?? 0m) +
                                    (lineDto.Q3 ?? 0m) + (lineDto.Q4 ?? 0m);

                if (quarterly > net)
                    errors.Add($"Activity {ai + 1} line {li + 1}: quarterly total ({quarterly:F2}) " +
                               $"exceeds net appropriation ({net:F2}).");
            }
        }
        if (errors.Count > 0)
            return ServiceResult<WfpRecordDto>.BadRequest(string.Join(" | ", errors));

        // ── Pass 2: load config snapshots ─────────────────────────────────────
        Dictionary<int, Account> accountDict =
            (await _accountRepo.GetAllAsync(ct)).ToDictionary(a => a.Id);
        Dictionary<int, FundingSource> fsDict =
            (await _fsRepo.GetAllAsync(ct)).ToDictionary(f => f.Id);

        // ── Pass 3: persist ───────────────────────────────────────────────────
        DateTime now = DateTime.UtcNow;
        WfpRecord wfpRecord;
        string auditAction;

        if (existing is not null)
        {
            // Delete all existing activities for this division's WFP record (cascade removes lines).
            IReadOnlyList<WfpActivity> oldActs = await _wfpRepo.GetActivitiesByWfpIdAsync(existing.Id, ct);
            foreach (WfpActivity act in oldActs)
                await _actRepo.DeleteAsync(act, ct);

            existing.UpdatedAt = now;
            await _wfpRepo.UpdateAsync(existing, ct);
            wfpRecord   = existing;
            auditAction = AuditAction.Update;
        }
        else
        {
            wfpRecord = new WfpRecord
            {
                AipRecordId = dto.AipRecordId,
                OfficeId    = dto.OfficeId,
                DivisionId  = dto.DivisionId,
                FiscalYear  = dto.FiscalYear,
                Status      = PlanningStatus.Draft,
                CreatedById = createdById,
                CreatedAt   = now,
                UpdatedAt   = now,
            };
            await _wfpRepo.AddAsync(wfpRecord, ct);
            await _wfpRepo.SaveChangesAsync(ct); // generate WfpRecord.Id
            auditAction = AuditAction.Create;
        }

        // Persist new activities and their expenditure lines.
        foreach (SaveWfpActivityDto actDto in dto.Activities)
        {
            WfpActivity activity = new()
            {
                WfpId         = wfpRecord.Id,
                AipActivityId = actDto.AipActivityId,
            };
            await _actRepo.AddAsync(activity, ct);
            await _actRepo.SaveChangesAsync(ct); // generate WfpActivity.Id

            foreach (SaveWfpExpenditureLineDto lineDto in actDto.Lines)
            {
                decimal reserveAmt = lineDto.ApplyReserve
                    ? Math.Round((lineDto.TotalAppropriation ?? 0m) * 0.1m, 2)
                    : 0m;
                decimal net       = (lineDto.TotalAppropriation ?? 0m) - reserveAmt;
                decimal quarterly = (lineDto.Q1 ?? 0m) + (lineDto.Q2 ?? 0m) +
                                    (lineDto.Q3 ?? 0m) + (lineDto.Q4 ?? 0m);

                accountDict.TryGetValue(lineDto.AccountId ?? 0, out Account? acct);
                fsDict.TryGetValue(lineDto.FundingSourceId ?? 0, out FundingSource? fs);

                WfpExpenditureLine line = new()
                {
                    WfpActivityId          = activity.Id,
                    ExpenditureType        = lineDto.ExpenditureType,
                    ResourcesNeeded        = lineDto.ResourcesNeeded,
                    ResponsibleUnit        = lineDto.ResponsibleUnit,
                    SuccessIndicator       = lineDto.SuccessIndicator,
                    MeansOfVerification    = lineDto.MeansOfVerification,
                    AccountId              = lineDto.AccountId,
                    AccountNumberSnapshot  = acct?.AccountNumber,
                    AccountTitleSnapshot   = acct?.AccountTitle,
                    TotalAppropriation     = lineDto.TotalAppropriation,
                    ApplyReserve           = lineDto.ApplyReserve,
                    ReserveAmount          = reserveAmt,
                    NetAppropriation       = net,
                    Q1                     = lineDto.Q1,
                    Q2                     = lineDto.Q2,
                    Q3                     = lineDto.Q3,
                    Q4                     = lineDto.Q4,
                    QuarterlyTotal         = quarterly,
                    FundingSourceId            = lineDto.FundingSourceId,
                    FundingSourceSnapshot      = fs?.Code,
                    FundingSourceNameSnapshot  = fs?.Name,
                    SortOrder                  = lineDto.SortOrder,
                };
                await _lineRepo.AddAsync(line, ct);
            }
        }
        await _lineRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("wfp_records", wfpRecord.Id, auditAction,
            auditAction == AuditAction.Create ? null : new { wfpRecord.Status },
            new { wfpRecord.AipRecordId, wfpRecord.OfficeId, wfpRecord.DivisionId, wfpRecord.Status }, ct);

        return ServiceResult<WfpRecordDto>.Ok(MapToDto(wfpRecord));
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<ServiceResult<WfpRecordDto>> FinalizeAsync(
        int id, CancellationToken ct = default)
    {
        WfpRecord? rec = await _wfpRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<WfpRecordDto>.NotFound($"WFP record {id} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<WfpRecordDto>.BadRequest($"Cannot finalize a record with status '{rec.Status}'.");

        // Backstop ceiling check (§8, RAL-122) — should be unreachable in practice once every
        // expenditure save is blocked, but kept as a safety net before locking the record.
        string? ceilingError = await _ceiling.ValidateRecordForFinalizeAsync(id, ct);
        if (ceilingError is not null)
            return ServiceResult<WfpRecordDto>.BadRequest(ceilingError);

        rec.Status      = PlanningStatus.Final;
        rec.FinalizedAt = DateTime.UtcNow;
        rec.UpdatedAt   = rec.FinalizedAt.Value;
        await _wfpRepo.UpdateAsync(rec, ct);
        await _wfpRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("wfp_records", rec.Id, AuditAction.Update,
            new { Status = PlanningStatus.Draft }, new { Status = PlanningStatus.Final }, ct);

        return ServiceResult<WfpRecordDto>.Ok(MapToDto(rec));
    }

    public async Task<ServiceResult<WfpRecordDto>> UnlockAsync(
        int id, CancellationToken ct = default)
    {
        WfpRecord? rec = await _wfpRepo.GetByIntIdAsync(id, ct);
        if (rec is null)
            return ServiceResult<WfpRecordDto>.NotFound($"WFP record {id} not found.");
        if (rec.Status != PlanningStatus.Final)
            return ServiceResult<WfpRecordDto>.BadRequest($"Cannot unlock a record with status '{rec.Status}'.");

        rec.Status      = PlanningStatus.Draft;
        rec.FinalizedAt = null;
        rec.UpdatedAt   = DateTime.UtcNow;
        await _wfpRepo.UpdateAsync(rec, ct);
        await _wfpRepo.SaveChangesAsync(ct);
        await _audit.LogAsync("wfp_records", rec.Id, AuditAction.Update,
            new { Status = PlanningStatus.Final }, new { Status = PlanningStatus.Draft }, ct);

        return ServiceResult<WfpRecordDto>.Ok(MapToDto(rec));
    }

    // ── v1.4 entry wizard enabler (RAL-123) ───────────────────────────────────

    public async Task<ServiceResult<WfpActivityRefDto>> EnsureActivityAsync(
        int aipRecordId, int officeId, int? divisionId, int fiscalYear, int aipActivityId,
        Guid createdById, CancellationToken ct = default)
    {
        WfpRecord? record = await _wfpRepo.FindByAipOfficeAndDivisionAsync(aipRecordId, officeId, divisionId, ct);

        if (record is not null && record.Status != PlanningStatus.Draft)
            return ServiceResult<WfpActivityRefDto>.Forbidden("Cannot add expenditures to a finalized WFP.");

        if (record is null)
        {
            DateTime now = DateTime.UtcNow;
            record = new WfpRecord
            {
                AipRecordId = aipRecordId,
                OfficeId    = officeId,
                DivisionId  = divisionId,
                FiscalYear  = fiscalYear,
                Status      = PlanningStatus.Draft,
                CreatedById = createdById,
                CreatedAt   = now,
                UpdatedAt   = now,
            };
            await _wfpRepo.AddAsync(record, ct);
            await _wfpRepo.SaveChangesAsync(ct); // generate record.Id
        }

        IReadOnlyList<WfpActivity> activities = await _wfpRepo.GetActivitiesByWfpIdAsync(record.Id, ct);
        WfpActivity? activity = activities.FirstOrDefault(a => a.AipActivityId == aipActivityId);

        if (activity is null)
        {
            activity = new WfpActivity { WfpId = record.Id, AipActivityId = aipActivityId };
            await _actRepo.AddAsync(activity, ct);
            await _actRepo.SaveChangesAsync(ct); // generate activity.Id
        }

        return ServiceResult<WfpActivityRefDto>.Ok(new WfpActivityRefDto(record.Id, activity.Id, record.Status));
    }

    // ── Export (RAL-79) ───────────────────────────────────────────────────────

    public async Task<ServiceResult<byte[]>> ExportReportAsync(
        int id, CancellationToken ct = default)
    {
        ServiceResult<WfpRecordDetailDto> wfpResult = await GetByIdAsync(id, ct);
        if (!wfpResult.IsSuccess)
            return ServiceResult<byte[]>.NotFound(wfpResult.Error!);

        WfpRecordDetailDto wfp = wfpResult.Value!;

        ServiceResult<AipRecordDetailDto> aipResult = await _aip.GetByIdAsync(wfp.AipRecordId, ct);
        if (!aipResult.IsSuccess)
            return ServiceResult<byte[]>.NotFound("Parent AIP record not found.");

        ServiceResult<OfficeDto> officeResult = await _office.GetByIdAsync(wfp.OfficeId, ct);
        string officeName = officeResult.IsSuccess ? officeResult.Value!.OfficeName : "PPDO";
        string officeCode = officeResult.IsSuccess ? officeResult.Value!.OfficeCode : "PPDO";

        IReadOnlyList<FundingSource> allFs = await _fsRepo.GetAllAsync(ct);
        Dictionary<int, string?> fsColors = allFs.ToDictionary(f => f.Id, f => f.Color);

        byte[] bytes = _excel.GenerateWfpReport(
            new WfpExcelReportData(wfp, aipResult.Value!, officeName, officeCode, fsColors));
        return ServiceResult<byte[]>.Ok(bytes);
    }

    // ── Purge (dev/test only) ─────────────────────────────────────────────────

    public async Task<int> PurgeAllAsync(CancellationToken ct = default)
    {
        IReadOnlyList<WfpRecord> all = await _wfpRepo.GetAllAsync(ct);
        foreach (WfpRecord rec in all)
            await _wfpRepo.DeleteAsync(rec, ct);
        if (all.Count > 0)
            await _wfpRepo.SaveChangesAsync(ct);
        return all.Count;
    }

    public async Task<WfpCleanupResultDto?> CleanupScopedAsync(
        int officeId, int? divisionId, int fiscalYear, CancellationToken ct = default)
    {
        WfpRecord? record = await _wfpRepo.FindByOfficeDivisionFiscalYearAsync(officeId, divisionId, fiscalYear, ct);
        if (record is null) return null;

        // Capture counts before deletion — DB cascade removes the child rows, so nothing is left
        // to count afterward.
        IReadOnlyList<WfpActivity> activities = await _wfpRepo.GetActivitiesByWfpIdAsync(record.Id, ct);

        int expenditureCount = 0;
        foreach (WfpActivity activity in activities)
            expenditureCount += (await _expenditureRepo.GetByWfpActivityIdAsync(activity.Id, ct)).Count;

        List<int> activityIds = activities.Select(a => a.Id).ToList();
        int legacyLineCount = (await _wfpRepo.GetLinesByActivityIdsAsync(activityIds, ct)).Count;

        bool wasFinal = record.Status == PlanningStatus.Final;

        // Unconditional — this is a live-testing reset tool (same category as PurgeAllAsync),
        // deliberately bypassing the normal Final-lock guard so a finalized WFP can be reset too.
        // No _audit.LogAsync here: audit entries require an authenticated CallerContext.UserId,
        // but this endpoint (like PurgeAllAsync/BudgetPlanningCleanup) is deliberately reachable
        // without a JWT — gated by the DevCleanupKey header instead. Same no-audit precedent as
        // PurgeAllAsync above.
        await _wfpRepo.DeleteAsync(record, ct);
        await _wfpRepo.SaveChangesAsync(ct);

        return new WfpCleanupResultDto(
            record.Id, officeId, divisionId, fiscalYear, wasFinal,
            activities.Count, expenditureCount, legacyLineCount);
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static WfpRecordDto MapToDto(WfpRecord r) => new(
        r.Id, r.AipRecordId, r.OfficeId, r.DivisionId, r.FiscalYear, r.Status,
        r.CreatedById, r.CreatedAt, r.UpdatedAt, r.FinalizedAt, r.SourceId);

    private static WfpExpenditureLineDto MapLineToDto(WfpExpenditureLine l) => new(
        l.Id, l.WfpActivityId, l.ExpenditureType, l.ResourcesNeeded, l.ResponsibleUnit,
        l.SuccessIndicator, l.MeansOfVerification, l.AccountId, l.AccountNumberSnapshot,
        l.AccountTitleSnapshot, l.TotalAppropriation, l.ApplyReserve, l.ReserveAmount,
        l.NetAppropriation, l.Q1, l.Q2, l.Q3, l.Q4, l.QuarterlyTotal,
        l.FundingSourceId, l.FundingSourceSnapshot, l.FundingSourceNameSnapshot, l.SortOrder);
}
