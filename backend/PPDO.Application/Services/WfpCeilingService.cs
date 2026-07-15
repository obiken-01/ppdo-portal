using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Implementation of <see cref="IWfpCeilingService"/> (RAL-122; fund-scoped v1.4.3 — RAL-154).
/// Reuses <see cref="IAllocationService.GetAllocationsAsync"/> for the current division-
/// allocation amount (never requeries what AllocationService already exposes) and the small
/// <c>IRepository&lt;Division&gt;</c>/<c>GetAllAsync</c>-then-filter pattern AllocationService
/// itself already uses for the same small config table.
///
/// The AIP-thousands-to-pesos ×1000 conversion happens ONLY in this class, at the point an
/// AipActivity.Total is compared against WFP peso amounts — never in AllocationService (whose
/// own doc comment explicitly forbids it) and never anywhere else in this service.
///
/// The AIP-budget check (step 1 everywhere below) stays aggregate across ALL funding sources
/// (§2 D3 — AIP data carries no per-fund breakdown). The division-allocation check (step 2) is
/// fund-scoped: every expenditure is checked against its OWN funding source's allocation, with
/// a null funding source resolved to General Fund via <see cref="IAllocationService.GetGeneralFundIdAsync"/>.
/// </summary>
public sealed class WfpCeilingService : IWfpCeilingService
{
    private readonly IWfpExpenditureRepository    _wfpExpRepo;
    private readonly IWfpRepository               _wfpRepo;
    private readonly IWfpAllocationLedgerRepository _ledgerRepo;
    private readonly IAipRepository               _aipRepo;
    private readonly IAllocationService           _allocation;
    private readonly IRepository<Division>        _divisionRepo;
    private readonly IRepository<FundingSource>   _fundingSourceRepo;

    public WfpCeilingService(
        IWfpExpenditureRepository      wfpExpRepo,
        IWfpRepository                 wfpRepo,
        IWfpAllocationLedgerRepository ledgerRepo,
        IAipRepository                 aipRepo,
        IAllocationService             allocation,
        IRepository<Division>          divisionRepo,
        IRepository<FundingSource>     fundingSourceRepo)
    {
        _wfpExpRepo        = wfpExpRepo;
        _wfpRepo           = wfpRepo;
        _ledgerRepo        = ledgerRepo;
        _aipRepo           = aipRepo;
        _allocation        = allocation;
        _divisionRepo      = divisionRepo;
        _fundingSourceRepo = fundingSourceRepo;
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

        int? gfId = await _allocation.GetGeneralFundIdAsync(ct);

        IReadOnlyList<FundingSource> activeFunds = (await _fundingSourceRepo.GetAllAsync(ct))
            .Where(f => f.IsActive)
            .ToList();

        List<WfpFundCeilingDto> funds = [];
        decimal gfAllocation = 0m, gfRemaining = 0m;

        foreach (FundingSource fund in activeFunds)
        {
            decimal allocation = await GetDivisionAllocationAsync(division, fiscalYear, fund.Id, ct);
            decimal usedForFund = await _ledgerRepo.SumUsedAmountAsync(
                divisionId, fiscalYear, fund.Id, excludeWfpRecordId: null, ct);
            decimal remaining = allocation - usedForFund;

            funds.Add(new WfpFundCeilingDto(fund.Id, fund.Code, fund.Name, allocation, remaining));

            if (gfId is int g && fund.Id == g)
            {
                gfAllocation = allocation;
                gfRemaining  = remaining;
            }
        }

        return new WfpCeilingStatusDto(aipBudget, aipUsed, gfAllocation, gfRemaining, funds);
    }

    // ── Block-on-save ─────────────────────────────────────────────────────────

    public async Task<string?> ValidateExpenditureSaveAsync(
        int wfpActivityId, decimal newExpenditureTotal, int? fundingSourceId,
        int? excludeExpenditureId, CancellationToken ct = default)
    {
        WfpExpenditureContext? context = await _wfpExpRepo.GetActivityContextAsync(wfpActivityId, ct);
        if (context is null) return null; // unknown activity — FK constraint will reject the save

        // ── 1. AIP budget check (across ALL divisions of the office, ALL funds — §2 D3) ──
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

        // ── 2. Division allocation check — fund-scoped (only when the WFP record has a division) ──
        if (context.DivisionId is int divisionId)
        {
            int? gfId = await _allocation.GetGeneralFundIdAsync(ct);
            int? resolvedFundId = fundingSourceId ?? gfId;
            if (resolvedFundId is null)
                return "Cannot validate the division allocation: no funding source is selected " +
                       "and General Fund is not configured.";

            Division? division = (await _divisionRepo.GetAllAsync(ct)).FirstOrDefault(d => d.Id == divisionId);
            decimal allocation = await GetDivisionAllocationAsync(division, context.FiscalYear, resolvedFundId.Value, ct);

            decimal otherRecordsUsed = await _ledgerRepo.SumUsedAmountAsync(
                divisionId, context.FiscalYear, resolvedFundId.Value, excludeWfpRecordId: context.WfpRecordId, ct);
            decimal othersInThisRecord = gfId is int g1
                ? await _wfpExpRepo.SumTotalByWfpRecordAsync(
                    context.WfpRecordId, resolvedFundId.Value, g1, excludeExpenditureId, ct)
                : 0m;
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

        int? gfId = await _allocation.GetGeneralFundIdAsync(ct);
        if (gfId is not int generalFundId)
            return; // cannot resolve null-fund expenditures to a concrete fund without GF configured

        // Union of (funds actually used by this record's expenditures right now, nulls
        // coalesced to GF) and (funds already tracked in the ledger for this record) — the
        // union ensures a fund that's no longer used still gets its row recomputed to zero
        // instead of left stale at its last positive amount.
        IReadOnlyList<int?> rawFundIds =
            await _wfpExpRepo.GetDistinctFundingSourceIdsByWfpRecordAsync(context.WfpRecordId, ct);
        IReadOnlyList<int> existingLedgerFundIds =
            await _ledgerRepo.GetFundingSourceIdsForRecordAsync(context.WfpRecordId, ct);

        HashSet<int> fundIds = rawFundIds.Select(f => f ?? generalFundId).ToHashSet();
        foreach (int existingFundId in existingLedgerFundIds)
            fundIds.Add(existingFundId);

        Division? division = (await _divisionRepo.GetAllAsync(ct)).FirstOrDefault(d => d.Id == divisionId);

        foreach (int fundId in fundIds)
        {
            decimal used = await _wfpExpRepo.SumTotalByWfpRecordAsync(
                context.WfpRecordId, fundId, generalFundId, excludeExpenditureId: null, ct);
            decimal allocation = await GetDivisionAllocationAsync(division, context.FiscalYear, fundId, ct);

            WfpDivisionAllocationLedger? row =
                await _ledgerRepo.FindAsync(divisionId, context.FiscalYear, fundId, context.WfpRecordId, ct);

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
                    FundingSourceId         = fundId,
                    WfpRecordId             = context.WfpRecordId,
                    AllocatedAmountSnapshot = allocation,
                    UsedAmount              = used,
                    UpdatedAt               = DateTime.UtcNow,
                };
                await _ledgerRepo.AddAsync(row, ct);
            }
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
            IReadOnlyList<int> fundIds = await _ledgerRepo.GetFundingSourceIdsForRecordAsync(wfpRecordId, ct);

            foreach (int fundId in fundIds)
            {
                decimal allocation = await GetDivisionAllocationAsync(division, record.FiscalYear, fundId, ct);
                decimal used = await _ledgerRepo.SumUsedAmountAsync(
                    divisionId, record.FiscalYear, fundId, excludeWfpRecordId: null, ct);

                if (used > allocation)
                    return $"Division allocation exceeded (₱{used:N2} used vs ₱{allocation:N2} allocated).";
            }
        }

        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<decimal> GetDivisionAllocationAsync(
        Division? division, int fiscalYear, int fundingSourceId, CancellationToken ct)
    {
        if (division is null) return 0m;
        IReadOnlyList<DivisionAllocationDto> allocs =
            await _allocation.GetAllocationsAsync(division.OfficeId, fiscalYear, fundingSourceId, ct);
        return allocs.FirstOrDefault(a => a.DivisionId == division.Id)?.Amount ?? 0m;
    }
}
