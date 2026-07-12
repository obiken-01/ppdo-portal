using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Implementation of <see cref="IWfpCeilingService"/> (RAL-122). Reuses
/// <see cref="IAllocationService.GetAllocationsAsync"/> for the current division-allocation
/// amount (never requeries what AllocationService already exposes) and the small
/// <c>IRepository&lt;Division&gt;</c>/<c>GetAllAsync</c>-then-filter pattern AllocationService
/// itself already uses for the same small config table.
///
/// The AIP-thousands-to-pesos ×1000 conversion happens ONLY in this class, at the point an
/// AipActivity.Total is compared against WFP peso amounts — never in AllocationService (whose
/// own doc comment explicitly forbids it) and never anywhere else in this service.
/// </summary>
public sealed class WfpCeilingService : IWfpCeilingService
{
    private readonly IWfpExpenditureRepository    _wfpExpRepo;
    private readonly IWfpRepository               _wfpRepo;
    private readonly IWfpAllocationLedgerRepository _ledgerRepo;
    private readonly IAipRepository               _aipRepo;
    private readonly IAllocationService           _allocation;
    private readonly IRepository<Division>        _divisionRepo;

    public WfpCeilingService(
        IWfpExpenditureRepository      wfpExpRepo,
        IWfpRepository                 wfpRepo,
        IWfpAllocationLedgerRepository ledgerRepo,
        IAipRepository                 aipRepo,
        IAllocationService             allocation,
        IRepository<Division>         divisionRepo)
    {
        _wfpExpRepo   = wfpExpRepo;
        _wfpRepo      = wfpRepo;
        _ledgerRepo   = ledgerRepo;
        _aipRepo      = aipRepo;
        _allocation   = allocation;
        _divisionRepo = divisionRepo;
    }

    // ── Read status ───────────────────────────────────────────────────────────

    public async Task<WfpCeilingStatusDto> GetStatusAsync(
        int aipActivityId, int divisionId, int fiscalYear, CancellationToken ct = default)
    {
        Division? division = (await _divisionRepo.GetAllAsync(ct)).FirstOrDefault(d => d.Id == divisionId);
        int officeId = division?.OfficeId ?? 0;

        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(aipActivityId, ct);
        decimal aipBudget = (activity?.Total ?? 0m) * 1000m; // the ONE conversion point

        decimal aipUsed = await _wfpExpRepo.SumTotalByAipActivityAsync(
            aipActivityId, officeId, fiscalYear, excludeExpenditureId: null, ct);

        decimal divisionAllocation = await GetDivisionAllocationAsync(division, fiscalYear, ct);
        decimal usedAcrossLedger = await _ledgerRepo.SumUsedAmountAsync(
            divisionId, fiscalYear, excludeWfpRecordId: null, ct);

        return new WfpCeilingStatusDto(aipBudget, aipUsed, divisionAllocation, divisionAllocation - usedAcrossLedger);
    }

    // ── Block-on-save ─────────────────────────────────────────────────────────

    public async Task<string?> ValidateExpenditureSaveAsync(
        int wfpActivityId, decimal newExpenditureTotal,
        int? excludeExpenditureId, CancellationToken ct = default)
    {
        WfpExpenditureContext? context = await _wfpExpRepo.GetActivityContextAsync(wfpActivityId, ct);
        if (context is null) return null; // unknown activity — FK constraint will reject the save

        // ── 1. AIP budget check (across ALL divisions of the office) ──────────
        AipActivity? activity = await _aipRepo.GetActivityByIdAsync(context.AipActivityId, ct);
        if (activity is not null)
        {
            decimal aipBudget = (activity.Total ?? 0m) * 1000m;
            decimal othersTotal = await _wfpExpRepo.SumTotalByAipActivityAsync(
                context.AipActivityId, context.OfficeId, context.FiscalYear, excludeExpenditureId, ct);
            decimal wouldBeUsed = othersTotal + newExpenditureTotal;

            if (wouldBeUsed > aipBudget)
                return $"This save would bring the AIP activity's total to ₱{wouldBeUsed:N2}, " +
                       $"exceeding its AIP budget of ₱{aipBudget:N2} by ₱{wouldBeUsed - aipBudget:N2}.";
        }

        // ── 2. Division allocation check (only when the WFP record has a division) ──
        if (context.DivisionId is int divisionId)
        {
            Division? division = (await _divisionRepo.GetAllAsync(ct)).FirstOrDefault(d => d.Id == divisionId);
            decimal allocation = await GetDivisionAllocationAsync(division, context.FiscalYear, ct);

            decimal otherRecordsUsed = await _ledgerRepo.SumUsedAmountAsync(
                divisionId, context.FiscalYear, excludeWfpRecordId: context.WfpRecordId, ct);
            decimal othersInThisRecord = await _wfpExpRepo.SumTotalByWfpRecordAsync(
                context.WfpRecordId, excludeExpenditureId, ct);
            decimal wouldBeThisRecordUsed = othersInThisRecord + newExpenditureTotal;
            decimal wouldBeDivisionUsed = otherRecordsUsed + wouldBeThisRecordUsed;

            if (wouldBeDivisionUsed > allocation)
                return $"This save would bring the division's allocated total to ₱{wouldBeDivisionUsed:N2}, " +
                       $"exceeding its division allocation of ₱{allocation:N2} by ₱{wouldBeDivisionUsed - allocation:N2}.";
        }

        return null;
    }

    // ── Ledger upsert ─────────────────────────────────────────────────────────

    public async Task UpsertLedgerForActivityAsync(int wfpActivityId, CancellationToken ct = default)
    {
        WfpExpenditureContext? context = await _wfpExpRepo.GetActivityContextAsync(wfpActivityId, ct);
        if (context?.DivisionId is not int divisionId)
            return; // no division on this WFP record — nothing to track

        decimal used = await _wfpExpRepo.SumTotalByWfpRecordAsync(context.WfpRecordId, excludeExpenditureId: null, ct);

        Division? division = (await _divisionRepo.GetAllAsync(ct)).FirstOrDefault(d => d.Id == divisionId);
        decimal allocation = await GetDivisionAllocationAsync(division, context.FiscalYear, ct);

        WfpDivisionAllocationLedger? row =
            await _ledgerRepo.FindAsync(divisionId, context.FiscalYear, context.WfpRecordId, ct);

        if (row is not null)
        {
            row.AllocatedAmountSnapshot = allocation;
            row.UsedAmount              = used;
            row.UpdatedAt               = DateTime.UtcNow;
            await _ledgerRepo.UpdateAsync(row, ct);
        }
        else
        {
            row = new WfpDivisionAllocationLedger
            {
                DivisionId              = divisionId,
                FiscalYear              = context.FiscalYear,
                WfpRecordId             = context.WfpRecordId,
                AllocatedAmountSnapshot = allocation,
                UsedAmount              = used,
                UpdatedAt               = DateTime.UtcNow,
            };
            await _ledgerRepo.AddAsync(row, ct);
        }

        await _ledgerRepo.SaveChangesAsync(ct);
    }

    // ── Finalize backstop ─────────────────────────────────────────────────────

    public async Task<string?> ValidateRecordForFinalizeAsync(int wfpRecordId, CancellationToken ct = default)
    {
        WfpRecord? record = await _wfpRepo.GetByIntIdAsync(wfpRecordId, ct);
        if (record is null) return null;

        IReadOnlyList<WfpActivity> activities = await _wfpRepo.GetActivitiesByWfpIdAsync(wfpRecordId, ct);
        foreach (int aipActivityId in activities.Select(a => a.AipActivityId).Distinct())
        {
            AipActivity? activity = await _aipRepo.GetActivityByIdAsync(aipActivityId, ct);
            if (activity is null) continue;

            decimal aipBudget = (activity.Total ?? 0m) * 1000m;
            decimal used = await _wfpExpRepo.SumTotalByAipActivityAsync(
                aipActivityId, record.OfficeId, record.FiscalYear, excludeExpenditureId: null, ct);

            if (used > aipBudget)
                return $"AIP activity {activity.RefCode} is over its AIP budget " +
                       $"(₱{used:N2} used vs ₱{aipBudget:N2} budgeted).";
        }

        if (record.DivisionId is int divisionId)
        {
            Division? division = (await _divisionRepo.GetAllAsync(ct)).FirstOrDefault(d => d.Id == divisionId);
            decimal allocation = await GetDivisionAllocationAsync(division, record.FiscalYear, ct);
            decimal used = await _ledgerRepo.SumUsedAmountAsync(divisionId, record.FiscalYear, excludeWfpRecordId: null, ct);

            if (used > allocation)
                return $"Division allocation exceeded (₱{used:N2} used vs ₱{allocation:N2} allocated).";
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<decimal> GetDivisionAllocationAsync(Division? division, int fiscalYear, CancellationToken ct)
    {
        if (division is null) return 0m;
        IReadOnlyList<DivisionAllocationDto> allocs =
            await _allocation.GetAllocationsAsync(division.OfficeId, fiscalYear, ct);
        return allocs.FirstOrDefault(a => a.DivisionId == division.Id)?.Amount ?? 0m;
    }
}
