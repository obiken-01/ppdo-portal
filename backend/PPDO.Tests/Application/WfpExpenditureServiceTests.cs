using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="WfpExpenditureService"/> (RAL-120, extended RAL-121).
/// Covers: create vs. update (delete-then-reinsert), snapshot population, validation,
/// procurement line_total computation, "server always recomputes from scratch on every
/// save" (never merges/retains stale totals), the reserve rule (no eligibility gate,
/// default = rate × Net, hard cap at rate × Net, explicit valid amounts respected as-is),
/// and that apply_reserve is never gated by the account's default_apply_reserve.
/// All repositories and IAuditService are mocked.
/// </summary>
public sealed class WfpExpenditureServiceTests
{
    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Account Acct(int id, string number, string title, bool defaultApplyReserve = false) => new()
    {
        Id = id, AccountNumber = number, AccountTitle = title, IsActive = true,
        ExpenseClass = "MOOE", DefaultApplyReserve = defaultApplyReserve,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static FundingSource Fs(int id, string code) => new()
    {
        Id = id, Code = code, Name = $"Fund {code}", IsActive = true,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static SaveWfpExpenditureDto QuarterlyDto(
        int? id = null, int wfpActivityId = 10, int? accountId = null, int? fsId = null,
        bool applyReserve = false, decimal? reserveAmount = null,
        decimal q1 = 100m, decimal q2 = 100m, decimal q3 = 100m, decimal q4 = 100m,
        string nature = WfpNature.NonProcurement) => new(
        id, wfpActivityId, accountId, nature, WfpFrequency.Quarterly, fsId,
        applyReserve, reserveAmount, null,
        [
            new SaveWfpExpenditurePeriodDto(1, q1),
            new SaveWfpExpenditurePeriodDto(2, q2),
            new SaveWfpExpenditurePeriodDto(3, q3),
            new SaveWfpExpenditurePeriodDto(4, q4),
        ],
        []);

    // ── Build ─────────────────────────────────────────────────────────────────

    private static (
        WfpExpenditureService sut,
        Mock<IWfpExpenditureRepository> repo,
        Mock<IRepository<WfpExpenditurePeriod>> periodRepo,
        Mock<IRepository<WfpProcurementItem>> itemRepo,
        Mock<IAuditService> audit)
        Build(List<Account> accountSeed, List<FundingSource> fsSeed)
    {
        List<WfpExpenditure> expSeed = [];
        List<WfpExpenditurePeriod> periodSeed = [];
        List<WfpProcurementItem> itemSeed = [];

        Mock<IWfpExpenditureRepository> repo = new();
        Mock<IRepository<WfpExpenditurePeriod>> periodRepo = new();
        Mock<IRepository<WfpProcurementItem>> itemRepo = new();
        Mock<IRepository<Account>> accountRepo = new();
        Mock<IRepository<FundingSource>> fsRepo = new();
        Mock<IAuditService> audit = new();

        int nextExpId = 100, nextPeriodId = 1000, nextItemId = 2000;

        repo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => expSeed.FirstOrDefault(e => e.Id == id));
        repo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditure>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditure, CancellationToken>((e, _) => { e.Id = nextExpId++; expSeed.Add(e); })
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<WfpExpenditure>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        repo.Setup(r => r.GetPeriodsByExpenditureIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int expId, CancellationToken _) =>
                (IReadOnlyList<WfpExpenditurePeriod>)periodSeed.Where(p => p.ExpenditureId == expId)
                    .OrderBy(p => p.PeriodNo).ToList());
        repo.Setup(r => r.GetProcurementItemsByExpenditureIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int expId, CancellationToken _) =>
                (IReadOnlyList<WfpProcurementItem>)itemSeed.Where(i => i.ExpenditureId == expId)
                    .OrderBy(i => i.PeriodNo).ToList());

        periodRepo.Setup(r => r.AddAsync(It.IsAny<WfpExpenditurePeriod>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditurePeriod, CancellationToken>((p, _) => { p.Id = nextPeriodId++; periodSeed.Add(p); })
            .Returns(Task.CompletedTask);
        periodRepo.Setup(r => r.DeleteAsync(It.IsAny<WfpExpenditurePeriod>(), It.IsAny<CancellationToken>()))
            .Callback<WfpExpenditurePeriod, CancellationToken>((p, _) => periodSeed.RemoveAll(x => x.Id == p.Id))
            .Returns(Task.CompletedTask);
        periodRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        itemRepo.Setup(r => r.AddAsync(It.IsAny<WfpProcurementItem>(), It.IsAny<CancellationToken>()))
            .Callback<WfpProcurementItem, CancellationToken>((i, _) => { i.Id = nextItemId++; itemSeed.Add(i); })
            .Returns(Task.CompletedTask);
        itemRepo.Setup(r => r.DeleteAsync(It.IsAny<WfpProcurementItem>(), It.IsAny<CancellationToken>()))
            .Callback<WfpProcurementItem, CancellationToken>((i, _) => itemSeed.RemoveAll(x => x.Id == i.Id))
            .Returns(Task.CompletedTask);
        itemRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        accountRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(accountSeed);
        fsRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(fsSeed);

        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        WfpExpenditureService sut = new(
            repo.Object, periodRepo.Object, itemRepo.Object, accountRepo.Object, fsRepo.Object, audit.Object);

        return (sut, repo, periodRepo, itemRepo, audit);
    }

    // ── Create vs update ──────────────────────────────────────────────────────

    [Fact]
    public async Task Save_NewExpenditure_CreatesRecord_WithComputedTotals()
    {
        var (sut, repo, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(q1: 100, q2: 200, q3: 300, q4: 400), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value!.Q1);
        Assert.Equal(200m, result.Value.Q2);
        Assert.Equal(300m, result.Value.Q3);
        Assert.Equal(400m, result.Value.Q4);
        Assert.Equal(1000m, result.Value.NetAppropriation);
        Assert.Equal(1000m, result.Value.TotalAppropriation);
        repo.Verify(r => r.AddAsync(It.IsAny<WfpExpenditure>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Save_UpdateNonexistentId_ReturnsNotFound()
    {
        var (sut, _, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(id: 999), CancellationToken.None);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task Save_UpdatingExpenditure_RecomputesTotalsFromScratch_IgnoringStaleValues()
    {
        // Server always recomputes even if the persisted row previously had different (stale)
        // totals — resaving with a new period set must NOT merge with or retain the old values.
        var (sut, repo, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> created = await sut.SaveExpenditureAsync(
            QuarterlyDto(q1: 100, q2: 100, q3: 100, q4: 100), CancellationToken.None);
        int id = created.Value!.Id;
        Assert.Equal(400m, created.Value.NetAppropriation);

        // Resave the SAME expenditure with entirely different amounts.
        ServiceResult<WfpExpenditureDto> updated = await sut.SaveExpenditureAsync(
            QuarterlyDto(id: id, q1: 5000, q2: 0, q3: 0, q4: 0), CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal(5000m, updated.Value!.Q1);
        Assert.Equal(0m, updated.Value.Q2);
        Assert.Equal(5000m, updated.Value.NetAppropriation);

        // Re-fetch independently to confirm persisted state matches — no leftover rows from
        // the first save merged in (still exactly 4 quarterly periods, not 8).
        ServiceResult<WfpExpenditureDto> refetched = await sut.GetByIdAsync(id, CancellationToken.None);
        Assert.Equal(5000m, refetched.Value!.NetAppropriation);
        Assert.Equal(4, refetched.Value.Periods.Count);
        Assert.Equal(5000m, refetched.Value.Periods.Single(p => p.PeriodNo == 1).Amount);
    }

    // ── Snapshot population ────────────────────────────────────────────────────

    [Fact]
    public async Task Save_PopulatesAccountSnapshot()
    {
        Account acct = Acct(5, "5-02-03-010", "Office Supplies Expenses");
        var (sut, _, _, _, _) = Build([acct], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(accountId: 5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("5-02-03-010", result.Value!.AccountNumberSnapshot);
        Assert.Equal("Office Supplies Expenses", result.Value.AccountTitleSnapshot);
    }

    [Fact]
    public async Task Save_PopulatesFundingSourceSnapshot()
    {
        FundingSource fs = Fs(7, "GF");
        var (sut, _, _, _, _) = Build([], [fs]);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(fsId: 7), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("GF", result.Value!.FundingSourceSnapshot);
        Assert.Equal("Fund GF", result.Value.FundingSourceNameSnapshot);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Save_InvalidNature_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto() with { Nature = "Bogus" }, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Save_InvalidFrequency_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto() with { Frequency = "X" }, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Save_PeriodOutOfRangeForFrequency_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], []);

        SaveWfpExpenditureDto dto = new(
            null, 10, null, WfpNature.NonProcurement, WfpFrequency.Quarterly, null,
            false, 0m, null,
            [new SaveWfpExpenditurePeriodDto(5, 100m)], // period 5 invalid for Quarterly (1-4)
            []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(dto, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Save_DuplicatePeriodNumber_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], []);

        SaveWfpExpenditureDto dto = new(
            null, 10, null, WfpNature.NonProcurement, WfpFrequency.Quarterly, null,
            false, 0m, null,
            [new SaveWfpExpenditurePeriodDto(1, 100m), new SaveWfpExpenditurePeriodDto(1, 200m)],
            []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(dto, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Save_NegativePeriodAmount_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(q1: -50m), CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Save_ProcurementItemMissingName_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], []);

        SaveWfpExpenditureDto dto = new(
            null, 10, null, WfpNature.Procurement, WfpFrequency.Quarterly, null,
            false, 0m, null, [],
            [new SaveWfpProcurementItemDto(1, null, "", "pc.", 10m, 2m)]);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(dto, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    // ── Procurement line_total computation ────────────────────────────────────

    [Fact]
    public async Task Save_ProcurementItem_ComputesLineTotalAsQtyTimesUnitPrice()
    {
        var (sut, _, _, _, _) = Build([], []);

        SaveWfpExpenditureDto dto = new(
            null, 10, null, WfpNature.Procurement, WfpFrequency.Quarterly, null,
            false, 0m, null, [],
            [new SaveWfpProcurementItemDto(1, null, "Bond paper", "ream", 250m, 4m)]);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.ProcurementItems);
        Assert.Equal(1000m, result.Value.ProcurementItems[0].LineTotal); // 4 x 250
        Assert.Equal(1000m, result.Value.Q1); // period total driven by the item
        Assert.Equal(1000m, result.Value.NetAppropriation);
    }

    [Fact]
    public async Task Save_NeverAcceptsClientLineTotal_AlwaysComputesFromQtyAndPrice()
    {
        // SaveWfpProcurementItemDto has no LineTotal field at all — architecturally the
        // client cannot send one. This test locks in that the persisted value is always
        // Qty x UnitPrice, regardless of how large/small those inputs are.
        var (sut, _, _, _, _) = Build([], []);

        SaveWfpExpenditureDto dto = new(
            null, 10, null, WfpNature.Procurement, WfpFrequency.Quarterly, null,
            false, 0m, null, [],
            [new SaveWfpProcurementItemDto(2, null, "Steel beam", "pc.", 15000.50m, 3m)]);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(45001.50m, result.Value!.ProcurementItems[0].LineTotal);
    }

    // ── Reserve (RAL-120 shape + RAL-121 rate/cap/default) ────────────────────

    [Fact]
    public async Task Save_ApplyReserveTrue_ExcludedFromNet_ButAddsToTotal()
    {
        var (sut, _, _, _, _) = Build([], []);

        // Net = 1000, rate 10% -> cap = 100. Explicit 100 is exactly at the cap.
        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(applyReserve: true, reserveAmount: 100m, q1: 250, q2: 250, q3: 250, q4: 250),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1000m, result.Value!.NetAppropriation);   // periods sum, reserve excluded
        Assert.Equal(1100m, result.Value.TotalAppropriation);  // Net + Reserved
        Assert.Equal(100m, result.Value.ReserveAmount);
    }

    [Fact]
    public async Task Save_ApplyReserveFalse_ForcesReserveAmountToZero_EvenIfDtoSuppliesOne()
    {
        var (sut, _, _, _, _) = Build([], []);

        // ApplyReserve=false skips the cap check entirely — 999 would otherwise be way over cap.
        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(applyReserve: false, reserveAmount: 999m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.ReserveAmount);
        Assert.Equal(result.Value.NetAppropriation, result.Value.TotalAppropriation);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ApplyReserveTrue_AnyAccountRegardlessOfDefaultApplyReserve_Succeeds(
        bool accountDefaultApplyReserve)
    {
        // No eligibility gate (RAL-117/121): toggling reserve on succeeds whether the account's
        // own default_apply_reserve agrees or disagrees with the caller's choice.
        Account acct = Acct(1, "5-02-03-010", "Office Supplies", accountDefaultApplyReserve);
        var (sut, _, _, _, _) = Build([acct], []);

        // Net = 400 (default q1..q4=100 each) -> cap = 40. Stay within cap.
        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(accountId: 1, applyReserve: true, reserveAmount: 30m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(30m, result.Value!.ReserveAmount);
    }

    [Fact]
    public async Task Save_ApplyReserve_FalseOnAccountWithDefaultApplyReserveTrue_Succeeds()
    {
        // The opposite override direction must also be unrestricted.
        Account acct = Acct(2, "5-02-13-020", "Repairs and Maintenance", defaultApplyReserve: true);
        var (sut, _, _, _, _) = Build([acct], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(accountId: 2, applyReserve: false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.ReserveAmount);
    }

    [Fact]
    public async Task Save_ApplyReserveTrue_NoAmountGiven_DefaultsToRateTimesNet()
    {
        var (sut, _, _, _, _) = Build([], []);

        // Net = 2000 (500 x 4 quarters), no ReserveAmount supplied -> default = 10% x 2000 = 200.
        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(applyReserve: true, reserveAmount: null, q1: 500, q2: 500, q3: 500, q4: 500),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2000m, result.Value!.NetAppropriation);
        Assert.Equal(200m, result.Value.ReserveAmount);
        Assert.Equal(2200m, result.Value.TotalAppropriation);
    }

    [Fact]
    public async Task Save_ApplyReserveTrue_ExplicitAmountExceedsCap_ReturnsBadRequest()
    {
        var (sut, _, _, _, _) = Build([], []);

        // Net = 400 -> cap = 40. Explicit 41 exceeds it.
        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(applyReserve: true, reserveAmount: 41m), CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("cap", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Save_ApplyReserveTrue_ExplicitAmountOverCap_RejectedRegardlessOfAccount()
    {
        // The cap is a flat rate rule, independent of any account eligibility flag.
        Account acct = Acct(3, "5-02-13-020", "Repairs and Maintenance", defaultApplyReserve: true);
        var (sut, _, _, _, _) = Build([acct], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(accountId: 3, applyReserve: true, reserveAmount: 500m), // way over the 40 cap
            CancellationToken.None);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task Save_ApplyReserveTrue_ExplicitValidAmount_IsRespected_NotOverriddenByDefault()
    {
        var (sut, _, _, _, _) = Build([], []);

        // Net = 400 -> default/cap would be 40, but an explicit, lower, valid amount (25) must
        // be kept exactly as given — never silently replaced by the 40 default.
        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(applyReserve: true, reserveAmount: 25m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(25m, result.Value!.ReserveAmount);
    }

    [Fact]
    public async Task Save_ApplyReserveTrue_ExplicitZero_IsRespectedAsZero_NotOverriddenByDefault()
    {
        // Distinguishes "0 given" from "nothing given" — only the latter triggers the default.
        var (sut, _, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(
            QuarterlyDto(applyReserve: true, reserveAmount: 0m), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value!.ReserveAmount);
    }

    [Fact]
    public void GetReserveRate_ReturnsTenPercent()
    {
        var (sut, _, _, _, _) = Build([], []);

        WfpReserveRateDto rate = sut.GetReserveRate();

        Assert.Equal(0.10m, rate.Rate);
    }

    // ── Combined nature (periods + procurement items together) ───────────────

    [Fact]
    public async Task Save_CombinedNature_MergesTypedPeriodAndProcurementItemsForSamePeriod()
    {
        var (sut, _, _, _, _) = Build([], []);

        SaveWfpExpenditureDto dto = new(
            null, 10, null, WfpNature.Combined, WfpFrequency.Quarterly, null,
            false, 0m, null,
            [new SaveWfpExpenditurePeriodDto(1, 500m)],
            [new SaveWfpProcurementItemDto(1, null, "Chairs", "pc.", 1000m, 2m)]); // 2000

        ServiceResult<WfpExpenditureDto> result = await sut.SaveExpenditureAsync(dto, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2500m, result.Value!.Q1); // 500 typed + 2000 procurement
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetById_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _) = Build([], []);

        ServiceResult<WfpExpenditureDto> result = await sut.GetByIdAsync(999, CancellationToken.None);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }
}
