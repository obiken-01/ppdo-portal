using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.BudgetPlanning;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Procurement items on an AIP expenditure line (V18-80 / PPDO-54).
///
/// <para>
/// The ticket's instruction is "lift the item-level machinery from WFP; leave the period machinery
/// behind", and the two hazards in doing that are both silent. The first is <b>double counting</b>:
/// WFP's <c>mergeWfpPeriodAndItemAmounts</c> <i>adds</i> item totals onto the typed amount, which
/// in a three-column AIP line would let an encoder who types the total and also itemises it pay
/// twice. The second is <b>column drift</b>: an AIP line has PS, MOOE and CO where WFP has one
/// amount, so an itemised total that lands in the wrong one still prints, still sums, and moves the
/// General Fund ceiling.
/// </para>
///
/// <para>
/// Every assertion below is a literal peso figure rather than a restatement of the formula — a test
/// that recomputes <c>qty × price × days</c> passes whatever the code happens to do.
/// </para>
/// </summary>
public sealed class AipExpenditureProcurementTests
{
    private const int ActivityId    = 41;
    private const int ProjectId     = 31;
    private const int ProgramId     = 21;
    private const int AipOfficeId   = 11;
    private const int AipRecordId   = 5;
    private const int ConfigOffice  = 7;
    private const int MooeAccountId = 100;
    private const int CoAccountId   = 101;
    private const int OddAccountId  = 102;

    // ── Fixture ───────────────────────────────────────────────────────────────

    private readonly Mock<IAipRepository>            _aipRepo  = new();
    private readonly Mock<IAipExpenditureRepository> _expRepo  = new();
    private readonly Mock<IAipActivityTotalsService> _totals   = new();
    private readonly Mock<IAipCeilingService>        _ceiling  = new();
    private readonly Mock<IRepository<Account>>      _accounts = new();
    private readonly Mock<IRepository<FundingSource>> _funds   = new();
    private readonly Mock<IAuditService>             _audit    = new();

    private readonly List<AipProcurementItem> _saved = [];

    private static readonly User Encoder = new()
    {
        Id = Guid.NewGuid(), Role = UserRole.Staff, OfficeId = ConfigOffice,
        Office = new Office { Id = ConfigOffice, OfficeCode = "GSO" },
    };

    public AipExpenditureProcurementTests()
    {
        _aipRepo.Setup(r => r.GetActivityByIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipActivity { Id = ActivityId, ProjectId = ProjectId });
        _aipRepo.Setup(r => r.GetProjectByIdAsync(ProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProject { Id = ProjectId, ProgramId = ProgramId });
        _aipRepo.Setup(r => r.GetProgramByIdAsync(ProgramId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipProgram { Id = ProgramId, OfficeId = AipOfficeId });
        _aipRepo.Setup(r => r.GetOfficeByIdAsync(AipOfficeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipOffice
            {
                Id = AipOfficeId, OfficeId = ConfigOffice, AipRecordId = AipRecordId,
                WorkflowStatus = AipWorkflowStatus.Draft,
            });
        _aipRepo.Setup(r => r.GetByIntIdAsync(AipRecordId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipRecord
            {
                Id = AipRecordId, FiscalYear = 2028, Status = PlanningStatus.Draft,
            });

        _accounts.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Account>
            {
                new() { Id = MooeAccountId, AccountNumber = "5-02-03-010", AccountTitle = "Office Supplies", ExpenseClass = "MOOE" },
                new() { Id = CoAccountId,   AccountNumber = "5-03-01-010", AccountTitle = "Machinery",       ExpenseClass = "CO" },
                new() { Id = OddAccountId,  AccountNumber = "9-99-99-999", AccountTitle = "Unclassified",    ExpenseClass = "" },
            });
        _funds.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FundingSource>());

        // The written line gets an id the way the database would, so the item write has one to hang
        // on — the service attaches items only after the parent save for exactly that reason.
        _expRepo.Setup(r => r.AddAsync(It.IsAny<AipExpenditure>(), It.IsAny<CancellationToken>()))
            .Callback<AipExpenditure, CancellationToken>((e, _) => e.Id = 900)
            .Returns(Task.CompletedTask);
        _expRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _expRepo.Setup(r => r.SumByActivityIdAsync(ActivityId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AipExpenditureTotalsDto(0m, 0m, 0m, 0m, 1));
        _expRepo.Setup(r => r.GetProcurementItemsByExpenditureIdsAsync(
                It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _saved);

        _expRepo.Setup(r => r.ReplaceProcurementItemsAsync(
                It.IsAny<int>(), It.IsAny<IReadOnlyList<AipProcurementItem>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<AipProcurementItem>, CancellationToken>((_, items, _) =>
            {
                _saved.Clear();
                _saved.AddRange(items);
            })
            .Returns(Task.CompletedTask);
    }

    private AipExpenditureService Build() => new(
        _aipRepo.Object, _expRepo.Object, _totals.Object, _ceiling.Object,
        _accounts.Object, _funds.Object, _audit.Object,
        NullLogger<AipExpenditureService>.Instance);

    private AipExpenditure? Written()
    {
        AipExpenditure? captured = null;
        _expRepo.Verify(r => r.AddAsync(
            It.Is<AipExpenditure>(e => Capture(e, ref captured)), It.IsAny<CancellationToken>()));
        return captured;
    }

    private static bool Capture(AipExpenditure e, ref AipExpenditure? into)
    {
        into = e;
        return true;
    }

    private static SaveAipProcurementItemDto Item(
        string name, decimal qty, decimal price, decimal days = 1m, int? priceIndexItemId = null)
        => new(priceIndexItemId, name, "pc", price, qty, days);

    // ── The roll-up ───────────────────────────────────────────────────────────

    /// <summary>
    /// Σ (qty × unitPrice × numberOfDays) lands in the column the <b>account</b> names, and only
    /// that column. 10 × ₱1,500 = ₱15,000 against a MOOE account.
    /// </summary>
    [Fact]
    public async Task Add_ItemsOnAMooeAccount_PutTheirTotalInMooeAndLeavePsAndCoAtZero()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, 0m, 0m, 0m,
            [Item("Bond paper", qty: 10m, price: 1_500m)]), Encoder);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m,      result.Value!.Line!.Ps);
        Assert.Equal(15_000m, result.Value.Line.Mooe);
        Assert.Equal(0m,      result.Value.Line.Co);
        Assert.Equal(15_000m, result.Value.Line.Total);
    }

    /// <summary>The same items against a Capital Outlay account land in CO instead. Same arithmetic, different column.</summary>
    [Fact]
    public async Task Add_ItemsOnACapitalOutlayAccount_PutTheirTotalInCo()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            CoAccountId, null, 0m, 0m, 0m,
            [Item("Generator", qty: 2m, price: 400_000m)]), Encoder);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m,        result.Value!.Line!.Mooe);
        Assert.Equal(800_000m,  result.Value.Line.Co);
    }

    /// <summary>
    /// ⚠️ <b>The double-count guard.</b> WFP's merge ADDS item totals to the typed amount. Here the
    /// encoder typed ₱99,999 of MOOE <i>and</i> itemised ₱15,000 — the line is worth ₱15,000, not
    /// ₱114,999. Once a line is itemised its amount is derived, and the typed figure is discarded.
    /// </summary>
    [Fact]
    public async Task Add_ItemsOverrideTheTypedAmountRatherThanAddingToIt()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, Ps: 0m, Mooe: 99_999m, Co: 0m,
            [Item("Bond paper", qty: 10m, price: 1_500m)]), Encoder);

        Assert.True(result.IsSuccess);
        Assert.Equal(15_000m, result.Value!.Line!.Mooe);
    }

    /// <summary>
    /// <c>numberOfDays</c> multiplies into the total. It is the one WFP field kept while every other
    /// schedule-shaped field was stripped, because PPDO employees asked for it — so it is pinned
    /// here in its own right. 3 people × ₱1,200 × 5 days = ₱18,000, not ₱3,600.
    /// </summary>
    [Fact]
    public async Task Add_NumberOfDays_MultipliesIntoTheLineTotal()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, 0m, 0m, 0m,
            [Item("Per diem", qty: 3m, price: 1_200m, days: 5m)]), Encoder);

        Assert.True(result.IsSuccess);
        Assert.Equal(18_000m, result.Value!.Line!.Mooe);
        Assert.Equal(18_000m, Assert.Single(result.Value.Line.ProcurementItems).LineTotal);
    }

    /// <summary>Several items sum before routing — ₱15,000 + ₱18,000.</summary>
    [Fact]
    public async Task Add_SeveralItems_SumBeforeTheyAreRouted()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, 0m, 0m, 0m,
            [Item("Bond paper", 10m, 1_500m), Item("Per diem", 3m, 1_200m, days: 5m)]), Encoder);

        Assert.True(result.IsSuccess);
        Assert.Equal(33_000m, result.Value!.Line!.Mooe);
    }

    /// <summary>A line with no items keeps the amounts the encoder typed — the pre-PPDO-54 path is untouched.</summary>
    [Fact]
    public async Task Add_WithNoItems_LeavesTheTypedAmountsExactlyAsGiven()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, Ps: 5_000m, Mooe: 20_000m, Co: 1_000m), Encoder);

        Assert.True(result.IsSuccess);
        Assert.Equal(5_000m,  result.Value!.Line!.Ps);
        Assert.Equal(20_000m, result.Value.Line.Mooe);
        Assert.Equal(1_000m,  result.Value.Line.Co);
        Assert.Empty(result.Value.Line.ProcurementItems);
    }

    // ── Refusals ──────────────────────────────────────────────────────────────

    /// <summary>
    /// An itemised line with no account has no column to land in. Refused rather than defaulted —
    /// MOOE is the obvious guess and it is the column the General Fund ceiling is computed from.
    /// </summary>
    [Fact]
    public async Task Add_ItemsWithNoAccount_IsRefusedRatherThanDefaultedToMooe()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            AccountId: null, null, 0m, 0m, 0m,
            [Item("Bond paper", 10m, 1_500m)]), Encoder);

        Assert.False(result.IsSuccess);
        Assert.Contains("needs an account", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>An account whose expense class is blank or unrecognised is refused, naming the account.</summary>
    [Fact]
    public async Task Add_ItemsOnAnAccountWithNoExpenseClass_IsRefusedAndNamesTheAccount()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            OddAccountId, null, 0m, 0m, 0m,
            [Item("Bond paper", 10m, 1_500m)]), Encoder);

        Assert.False(result.IsSuccess);
        Assert.Contains("9-99-99-999", result.Error);
    }

    /// <summary>
    /// Zero days would silently zero a line the encoder just costed, which reads as the save having
    /// failed. Days default to 1 for non-day-based items, so zero is never what someone meant.
    /// </summary>
    [Fact]
    public async Task Add_ItemWithZeroDays_IsRefusedAndSaysToUseOne()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, 0m, 0m, 0m,
            [Item("Per diem", 3m, 1_200m, days: 0m)]), Encoder);

        Assert.False(result.IsSuccess);
        Assert.Contains("Use 1", result.Error);
    }

    [Fact]
    public async Task Add_ItemWithNegativeQuantity_IsRefused()
    {
        var result = await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, 0m, 0m, 0m,
            [Item("Bond paper", qty: -1m, price: 1_500m)]), Encoder);

        Assert.False(result.IsSuccess);
        Assert.Contains("negative", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    // ── LineTotal is the server's, not the client's ───────────────────────────

    /// <summary>
    /// The save DTO carries no <c>lineTotal</c> field at all, and the entity's setter is private —
    /// so a client cannot state a total that disagrees with its own factors. This pins that the
    /// stored figure is the computed one (RAL-144's rule, applied one level down).
    /// </summary>
    [Fact]
    public async Task Add_LineTotalOnEachSavedItem_IsComputedServerSide()
    {
        await Build().AddAsync(ActivityId, new CreateAipExpenditureDto(
            MooeAccountId, null, 0m, 0m, 0m,
            [Item("Per diem", qty: 3m, price: 1_200m, days: 5m)]), Encoder);

        AipProcurementItem stored = Assert.Single(_saved);
        Assert.Equal(18_000m, stored.LineTotal);
    }
}
