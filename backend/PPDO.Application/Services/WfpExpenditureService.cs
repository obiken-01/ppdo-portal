using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// WFP expenditure save + read (v1.4 WFP Rework — RAL-120). Owns the single computation
/// pipeline (<see cref="WfpExpenditureCalculator"/>): period amounts (or Σ procurement items
/// per period) roll up to Q1–Q4 -> Net -> Total, always computed here on save — never
/// accepted from the client, and never re-derived anywhere else (the report generator, when
/// built, must call this same calculator).
///
/// SaveExpenditureAsync: create when dto.Id is null, else replace the existing expenditure's
/// periods/procurement items in place (delete-then-reinsert, matching the LdipService/WfpService
/// convention) so the unique (expenditure_id, period_no) index never sees old+new side by side.
///
/// Does NOT validate apply_reserve against the account's default_apply_reserve — RAL-117/121
/// made that a default-only pre-fill, never an enforced gate; any expenditure may set it
/// regardless of the account's default. Does NOT enforce the 10% reserve cap — that
/// validation is RAL-121's scope, to be added to this same method.
/// </summary>
public sealed class WfpExpenditureService : IWfpExpenditureService
{
    private readonly IWfpExpenditureRepository            _repo;
    private readonly IRepository<WfpExpenditurePeriod>    _periodRepo;
    private readonly IRepository<WfpProcurementItem>      _itemRepo;
    private readonly IRepository<Account>                 _accountRepo;
    private readonly IRepository<FundingSource>           _fsRepo;
    private readonly IAuditService                        _audit;

    public WfpExpenditureService(
        IWfpExpenditureRepository         repo,
        IRepository<WfpExpenditurePeriod> periodRepo,
        IRepository<WfpProcurementItem>   itemRepo,
        IRepository<Account>              accountRepo,
        IRepository<FundingSource>        fsRepo,
        IAuditService                     audit)
    {
        _repo        = repo;
        _periodRepo  = periodRepo;
        _itemRepo    = itemRepo;
        _accountRepo = accountRepo;
        _fsRepo      = fsRepo;
        _audit       = audit;
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    public async Task<ServiceResult<WfpExpenditureDto>> GetByIdAsync(
        int id, CancellationToken ct = default)
    {
        WfpExpenditure? entity = await _repo.GetByIntIdAsync(id, ct);
        if (entity is null)
            return ServiceResult<WfpExpenditureDto>.NotFound($"WFP expenditure {id} not found.");

        IReadOnlyList<WfpExpenditurePeriod> periods = await _repo.GetPeriodsByExpenditureIdAsync(id, ct);
        IReadOnlyList<WfpProcurementItem>   items   = await _repo.GetProcurementItemsByExpenditureIdAsync(id, ct);
        return ServiceResult<WfpExpenditureDto>.Ok(MapToDto(entity, periods, items));
    }

    // ── Save (create or replace) ─────────────────────────────────────────────

    public async Task<ServiceResult<WfpExpenditureDto>> SaveExpenditureAsync(
        SaveWfpExpenditureDto dto, CancellationToken ct = default)
    {
        string? validationError = ValidateDto(dto);
        if (validationError is not null)
            return ServiceResult<WfpExpenditureDto>.BadRequest(validationError);

        WfpExpenditure? existing = null;
        if (dto.Id.HasValue)
        {
            existing = await _repo.GetByIntIdAsync(dto.Id.Value, ct);
            if (existing is null)
                return ServiceResult<WfpExpenditureDto>.NotFound($"WFP expenditure {dto.Id} not found.");
        }

        // ── Snapshots (small config tables — same pattern as WfpService) ─────
        Account? account = dto.AccountId.HasValue
            ? (await _accountRepo.GetAllAsync(ct)).FirstOrDefault(a => a.Id == dto.AccountId.Value)
            : null;
        FundingSource? fs = dto.FundingSourceId.HasValue
            ? (await _fsRepo.GetAllAsync(ct)).FirstOrDefault(f => f.Id == dto.FundingSourceId.Value)
            : null;

        // ── Compute (never trust client-sent totals) ─────────────────────────
        decimal reserveAmount = dto.ApplyReserve ? dto.ReserveAmount : 0m;
        int? annualQuarterChoice = dto.Frequency == WfpFrequency.Annual
            ? (dto.AnnualQuarterChoice ?? 1)
            : null;

        Dictionary<int, decimal> merged = WfpExpenditureCalculator.MergePeriodAmounts(
            dto.Periods.Select(p => (p.PeriodNo, p.Amount)),
            dto.ProcurementItems.Select(i => (i.PeriodNo, i.Qty, i.UnitPrice)));
        WfpExpenditureCalculator.RollUp rollUp = WfpExpenditureCalculator.Compute(
            dto.Frequency, merged, reserveAmount, annualQuarterChoice);

        DateTime now = DateTime.UtcNow;
        WfpExpenditure entity;
        string auditAction;

        if (existing is not null)
        {
            // Delete-then-reinsert children (LdipService/WfpService convention) — flushed
            // together with the inserts below in the single SaveChangesAsync at the end.
            foreach (WfpExpenditurePeriod p in await _repo.GetPeriodsByExpenditureIdAsync(existing.Id, ct))
                await _periodRepo.DeleteAsync(p, ct);
            foreach (WfpProcurementItem i in await _repo.GetProcurementItemsByExpenditureIdAsync(existing.Id, ct))
                await _itemRepo.DeleteAsync(i, ct);

            existing.AccountId                 = dto.AccountId;
            existing.AccountNumberSnapshot      = account?.AccountNumber;
            existing.AccountTitleSnapshot       = account?.AccountTitle;
            existing.Nature                     = dto.Nature;
            existing.Frequency                  = dto.Frequency;
            existing.FundingSourceId            = dto.FundingSourceId;
            existing.FundingSourceSnapshot      = fs?.Code;
            existing.FundingSourceNameSnapshot  = fs?.Name;
            existing.ApplyReserve               = dto.ApplyReserve;
            existing.ReserveAmount              = reserveAmount;
            existing.AnnualQuarterChoice        = annualQuarterChoice;
            existing.Q1                         = rollUp.Q1;
            existing.Q2                         = rollUp.Q2;
            existing.Q3                         = rollUp.Q3;
            existing.Q4                         = rollUp.Q4;
            existing.NetAppropriation           = rollUp.Net;
            existing.TotalAppropriation         = rollUp.Total;
            existing.UpdatedAt                  = now;

            await _repo.UpdateAsync(existing, ct);
            entity      = existing;
            auditAction = AuditAction.Update;
        }
        else
        {
            entity = new WfpExpenditure
            {
                WfpActivityId             = dto.WfpActivityId,
                AccountId                 = dto.AccountId,
                AccountNumberSnapshot     = account?.AccountNumber,
                AccountTitleSnapshot      = account?.AccountTitle,
                Nature                    = dto.Nature,
                Frequency                 = dto.Frequency,
                FundingSourceId           = dto.FundingSourceId,
                FundingSourceSnapshot     = fs?.Code,
                FundingSourceNameSnapshot = fs?.Name,
                ApplyReserve              = dto.ApplyReserve,
                ReserveAmount             = reserveAmount,
                AnnualQuarterChoice       = annualQuarterChoice,
                Q1                        = rollUp.Q1,
                Q2                        = rollUp.Q2,
                Q3                        = rollUp.Q3,
                Q4                        = rollUp.Q4,
                NetAppropriation          = rollUp.Net,
                TotalAppropriation        = rollUp.Total,
                CreatedAt                 = now,
                UpdatedAt                 = now,
            };
            await _repo.AddAsync(entity, ct);
            await _repo.SaveChangesAsync(ct); // generate entity.Id before children reference it
            auditAction = AuditAction.Create;
        }

        foreach (SaveWfpExpenditurePeriodDto p in dto.Periods)
        {
            await _periodRepo.AddAsync(new WfpExpenditurePeriod
            {
                ExpenditureId = entity.Id,
                PeriodNo      = p.PeriodNo,
                Amount        = p.Amount,
            }, ct);
        }

        foreach (SaveWfpProcurementItemDto i in dto.ProcurementItems)
        {
            await _itemRepo.AddAsync(new WfpProcurementItem
            {
                ExpenditureId    = entity.Id,
                PeriodNo         = i.PeriodNo,
                PriceIndexItemId = i.PriceIndexItemId,
                Name             = i.Name,
                Unit             = i.Unit,
                UnitPrice        = i.UnitPrice,
                Qty              = i.Qty,
                LineTotal        = Math.Round(i.Qty * i.UnitPrice, 2),
            }, ct);
        }

        await _repo.SaveChangesAsync(ct);

        await _audit.LogAsync("wfp_expenditures", entity.Id, auditAction,
            oldValues: auditAction == AuditAction.Create ? null : new { existing!.Nature, existing.Frequency, existing.TotalAppropriation },
            newValues: new { entity.Nature, entity.Frequency, entity.TotalAppropriation }, ct);

        IReadOnlyList<WfpExpenditurePeriod> savedPeriods = await _repo.GetPeriodsByExpenditureIdAsync(entity.Id, ct);
        IReadOnlyList<WfpProcurementItem>   savedItems   = await _repo.GetProcurementItemsByExpenditureIdAsync(entity.Id, ct);
        return ServiceResult<WfpExpenditureDto>.Ok(MapToDto(entity, savedPeriods, savedItems));
    }

    // ── Validation ────────────────────────────────────────────────────────────

    private static string? ValidateDto(SaveWfpExpenditureDto dto)
    {
        if (dto.Nature is not (WfpNature.Procurement or WfpNature.NonProcurement or WfpNature.Combined))
            return $"Nature must be one of Procurement, Non-Procurement, or Combined (got '{dto.Nature}').";

        (int min, int max) = WfpExpenditureCalculator.PeriodRange(dto.Frequency);
        if (max < min)
            return $"Frequency must be one of M, Q, B, or A (got '{dto.Frequency}').";

        if (dto.ReserveAmount < 0)
            return "Reserve amount cannot be negative.";

        if (dto.AnnualQuarterChoice is int choice && choice is < 1 or > 4)
            return "Annual quarter choice must be between 1 and 4.";

        HashSet<int> seenPeriods = new();
        foreach (SaveWfpExpenditurePeriodDto p in dto.Periods)
        {
            if (p.PeriodNo < min || p.PeriodNo > max)
                return $"Period {p.PeriodNo} is out of range for frequency '{dto.Frequency}' (expected {min}-{max}).";
            if (!seenPeriods.Add(p.PeriodNo))
                return $"Duplicate period {p.PeriodNo} in the typed period amounts.";
            if (p.Amount < 0)
                return $"Period {p.PeriodNo} amount cannot be negative.";
        }

        foreach (SaveWfpProcurementItemDto i in dto.ProcurementItems)
        {
            if (i.PeriodNo < min || i.PeriodNo > max)
                return $"Procurement item period {i.PeriodNo} is out of range for frequency '{dto.Frequency}' (expected {min}-{max}).";
            if (string.IsNullOrWhiteSpace(i.Name))
                return "Procurement item name is required.";
            if (string.IsNullOrWhiteSpace(i.Unit))
                return "Procurement item unit is required.";
            if (i.UnitPrice < 0)
                return "Procurement item unit price cannot be negative.";
            if (i.Qty < 0)
                return "Procurement item quantity cannot be negative.";
        }

        return null;
    }

    // ── Mapping ───────────────────────────────────────────────────────────────

    private static WfpExpenditureDto MapToDto(
        WfpExpenditure entity,
        IReadOnlyList<WfpExpenditurePeriod> periods,
        IReadOnlyList<WfpProcurementItem> items) => new(
        entity.Id, entity.WfpActivityId, entity.AccountId, entity.AccountNumberSnapshot, entity.AccountTitleSnapshot,
        entity.Nature, entity.Frequency, entity.FundingSourceId, entity.FundingSourceSnapshot, entity.FundingSourceNameSnapshot,
        entity.ApplyReserve, entity.ReserveAmount, entity.AnnualQuarterChoice,
        entity.Q1, entity.Q2, entity.Q3, entity.Q4, entity.NetAppropriation, entity.TotalAppropriation,
        periods.Select(p => new WfpExpenditurePeriodDto(p.PeriodNo, p.Amount)).ToList(),
        items.Select(i => new WfpProcurementItemDto(
            i.PeriodNo, i.PriceIndexItemId, i.Name, i.Unit, i.UnitPrice, i.Qty, i.LineTotal)).ToList());
}
