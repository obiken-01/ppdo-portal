using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// WFP service — upsert, expenditure-line validation, snapshot population,
/// status lifecycle (RAL-64), and Excel export (RAL-79).
///
/// SaveAsync two-pass logic:
///   1. Validate all lines (quarterly_total ≤ net_appropriation).
///   2. Load account + funding-source dicts for snapshot population.
///   3. Delete existing activities (cascade removes lines) when updating a Draft.
///   4. Persist new activities and lines.
///
/// Reserve computation: reserveAmount = Round(totalAppropriation × 10%, 2) when ApplyReserve=true.
/// </summary>
public sealed class WfpService : IWfpService
{
    private readonly IRepository<WfpRecord>          _wfpRepo;
    private readonly IRepository<WfpActivity>        _actRepo;
    private readonly IRepository<WfpExpenditureLine> _lineRepo;
    private readonly IRepository<Account>            _accountRepo;
    private readonly IRepository<FundingSource>      _fsRepo;
    private readonly IAuditService                   _audit;
    private readonly CallerContext                   _caller;
    private readonly IAipService                     _aip;
    private readonly IOfficeService                  _office;
    private readonly IWfpExcelService                _excel;

    public WfpService(
        IRepository<WfpRecord>          wfpRepo,
        IRepository<WfpActivity>        actRepo,
        IRepository<WfpExpenditureLine> lineRepo,
        IRepository<Account>            accountRepo,
        IRepository<FundingSource>      fsRepo,
        IAuditService                   audit,
        CallerContext                   caller,
        IAipService                     aip,
        IOfficeService                  office,
        IWfpExcelService                excel)
    {
        _wfpRepo     = wfpRepo;
        _actRepo     = actRepo;
        _lineRepo    = lineRepo;
        _accountRepo = accountRepo;
        _fsRepo      = fsRepo;
        _audit       = audit;
        _caller      = caller;
        _aip         = aip;
        _office      = office;
        _excel       = excel;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<WfpRecordDto>> GetAllAsync(
        int? aipRecordId, int? officeId, CancellationToken ct = default)
    {
        IEnumerable<WfpRecord> q = await _wfpRepo.GetAllAsync(ct);
        if (aipRecordId.HasValue) q = q.Where(r => r.AipRecordId == aipRecordId.Value);
        if (officeId.HasValue)    q = q.Where(r => r.OfficeId == officeId.Value);
        return q.OrderByDescending(r => r.UpdatedAt).Select(MapToDto).ToList();
    }

    public async Task<ServiceResult<WfpRecordDetailDto>> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        WfpRecord? rec = (await _wfpRepo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
        if (rec is null)
            return ServiceResult<WfpRecordDetailDto>.NotFound($"WFP record {id} not found.");

        IReadOnlyList<WfpActivity> activities =
            (await _actRepo.GetAllAsync(ct)).Where(a => a.WfpId == id).ToList();
        List<int> actIds = activities.Select(a => a.Id).ToList();
        IReadOnlyList<WfpExpenditureLine> lines =
            (await _lineRepo.GetAllAsync(ct)).Where(l => actIds.Contains(l.WfpActivityId)).ToList();

        IReadOnlyList<WfpActivityDto> actDtos = activities.Select(a =>
            new WfpActivityDto(a.Id, a.WfpId, a.AipActivityId,
                lines.Where(l => l.WfpActivityId == a.Id).Select(MapLineToDto).ToList())).ToList();

        return ServiceResult<WfpRecordDetailDto>.Ok(new WfpRecordDetailDto(
            rec.Id, rec.AipRecordId, rec.OfficeId, rec.FiscalYear, rec.Status,
            rec.CreatedById, rec.CreatedAt, rec.UpdatedAt, rec.FinalizedAt, rec.SourceId, actDtos));
    }

    // ── Save (upsert) ─────────────────────────────────────────────────────────

    public async Task<ServiceResult<WfpRecordDto>> SaveAsync(
        SaveWfpDto dto, Guid createdById, CancellationToken ct = default)
    {
        // Find existing WFP for this (aipRecordId, officeId).
        WfpRecord? existing = (await _wfpRepo.GetAllAsync(ct))
            .FirstOrDefault(r => r.AipRecordId == dto.AipRecordId && r.OfficeId == dto.OfficeId);

        if (existing is not null && existing.Status == PlanningStatus.Final)
            return ServiceResult<WfpRecordDto>.Forbidden("Cannot edit a finalized WFP.");

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
            // Delete all existing activities (cascade removes lines in DB).
            IReadOnlyList<WfpActivity> oldActs =
                (await _actRepo.GetAllAsync(ct)).Where(a => a.WfpId == existing.Id).ToList();
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
                    FundingSourceId        = lineDto.FundingSourceId,
                    FundingSourceSnapshot  = fs?.Code,
                    SortOrder              = lineDto.SortOrder,
                };
                await _lineRepo.AddAsync(line, ct);
            }
        }
        await _lineRepo.SaveChangesAsync(ct);

        await _audit.LogAsync("wfp_records", wfpRecord.Id, auditAction,
            auditAction == AuditAction.Create ? null : new { wfpRecord.Status },
            new { wfpRecord.AipRecordId, wfpRecord.OfficeId, wfpRecord.Status }, ct);

        return ServiceResult<WfpRecordDto>.Ok(MapToDto(wfpRecord));
    }

    // ── Status transitions ────────────────────────────────────────────────────

    public async Task<ServiceResult<WfpRecordDto>> FinalizeAsync(
        int id, CancellationToken ct = default)
    {
        WfpRecord? rec = (await _wfpRepo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
        if (rec is null)
            return ServiceResult<WfpRecordDto>.NotFound($"WFP record {id} not found.");
        if (rec.Status != PlanningStatus.Draft)
            return ServiceResult<WfpRecordDto>.BadRequest($"Cannot finalize a record with status '{rec.Status}'.");

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
        WfpRecord? rec = (await _wfpRepo.GetAllAsync(ct)).FirstOrDefault(r => r.Id == id);
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

        byte[] bytes = _excel.GenerateWfpReport(
            new WfpExcelReportData(wfp, aipResult.Value!, officeName, officeCode));
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

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static WfpRecordDto MapToDto(WfpRecord r) => new(
        r.Id, r.AipRecordId, r.OfficeId, r.FiscalYear, r.Status,
        r.CreatedById, r.CreatedAt, r.UpdatedAt, r.FinalizedAt, r.SourceId);

    private static WfpExpenditureLineDto MapLineToDto(WfpExpenditureLine l) => new(
        l.Id, l.WfpActivityId, l.ExpenditureType, l.ResourcesNeeded, l.ResponsibleUnit,
        l.SuccessIndicator, l.MeansOfVerification, l.AccountId, l.AccountNumberSnapshot,
        l.AccountTitleSnapshot, l.TotalAppropriation, l.ApplyReserve, l.ReserveAmount,
        l.NetAppropriation, l.Q1, l.Q2, l.Q3, l.Q4, l.QuarterlyTotal,
        l.FundingSourceId, l.FundingSourceSnapshot, l.SortOrder);
}
