using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ProcurementPresetService"/> (RAL-119): create/update/delete,
/// item snapshot semantics (picking a price_index_item snapshots Name/Unit/UnitPrice at save
/// time — a later price change on that price index item must NOT retroactively change the
/// preset's already-saved values), free-typed items with no price-index link, and validation.
/// All repositories and IAuditService are mocked.
/// </summary>
public sealed class ProcurementPresetServiceTests
{
    // ── Seed helpers ──────────────────────────────────────────────────────────

    private static Account Acct(int id, string number = "5-02-03-010", string title = "Office Supplies Expenses") => new()
    {
        Id = id, AccountNumber = number, AccountTitle = title, IsActive = true,
        ExpenseClass = "MOOE", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static PriceIndexItem PriceItem(int id, string name, string unit, decimal price) => new()
    {
        Id = id, Name = name, Unit = unit, UnitPrice = price, IsActive = true,
        PriceUpdatedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static User Caller(Guid? id = null) => new()
    {
        Id = id ?? Guid.NewGuid(), FullName = "Test User", Username = "testuser",
        PasswordHash = "x", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    // ── Build ─────────────────────────────────────────────────────────────────

    private static (
        ProcurementPresetService sut,
        Mock<IProcurementPresetRepository> repo,
        Mock<IRepository<ProcurementPresetItem>> itemRepo,
        List<PriceIndexItem> priceIndexSeed,
        Mock<IAuditService> audit,
        Mock<IPriceIndexItemRepository> priceIndexRepo)
        Build(List<Account> accountSeed, List<PriceIndexItem> priceIndexSeed)
    {
        List<ProcurementPreset> presetSeed = [];
        List<ProcurementPresetItem> itemSeed = [];

        Mock<IProcurementPresetRepository> repo = new();
        Mock<IRepository<ProcurementPresetItem>> itemRepo = new();
        Mock<IRepository<Account>> accountRepo = new();
        Mock<IPriceIndexItemRepository> priceIndexRepo = new();
        Mock<IAuditService> audit = new();

        int nextPresetId = 1, nextItemId = 1000;

        void Relink()
        {
            foreach (ProcurementPreset p in presetSeed)
                p.Items = itemSeed.Where(i => i.PresetId == p.Id).OrderBy(i => i.Id).ToList();
        }

        repo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => { Relink(); return presetSeed.FirstOrDefault(p => p.Id == id); });
        repo.Setup(r => r.GetByAccountIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int accId, CancellationToken _) =>
            {
                Relink();
                return (IReadOnlyList<ProcurementPreset>)presetSeed.Where(p => p.AccountId == accId).OrderBy(p => p.Name).ToList();
            });
        repo.Setup(r => r.GetAllWithItemsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((CancellationToken _) =>
            {
                Relink();
                return (IReadOnlyList<ProcurementPreset>)presetSeed
                    .OrderBy(p => accountSeed.FirstOrDefault(a => a.Id == p.AccountId)?.AccountNumber)
                    .ThenBy(p => p.Name)
                    .ToList();
            });
        repo.Setup(r => r.AddAsync(It.IsAny<ProcurementPreset>(), It.IsAny<CancellationToken>()))
            .Callback<ProcurementPreset, CancellationToken>((p, _) => { p.Id = nextPresetId++; presetSeed.Add(p); })
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<ProcurementPreset>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        itemRepo.Setup(r => r.AddAsync(It.IsAny<ProcurementPresetItem>(), It.IsAny<CancellationToken>()))
            .Callback<ProcurementPresetItem, CancellationToken>((i, _) => { i.Id = nextItemId++; itemSeed.Add(i); })
            .Returns(Task.CompletedTask);
        itemRepo.Setup(r => r.DeleteAsync(It.IsAny<ProcurementPresetItem>(), It.IsAny<CancellationToken>()))
            .Callback<ProcurementPresetItem, CancellationToken>((i, _) => itemSeed.RemoveAll(x => x.Id == i.Id))
            .Returns(Task.CompletedTask);
        itemRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        accountRepo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(accountSeed);
        priceIndexRepo.Setup(r => r.GetByIdsAsync(It.IsAny<IReadOnlyList<int>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<int> ids, CancellationToken _) =>
                (IReadOnlyList<PriceIndexItem>)priceIndexSeed.Where(p => ids.Contains(p.Id)).ToList());

        audit.Setup(a => a.LogAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ProcurementPresetService sut = new(
            repo.Object, itemRepo.Object, accountRepo.Object, priceIndexRepo.Object,
            audit.Object, NullLogger<ProcurementPresetService>.Instance);

        return (sut, repo, itemRepo, priceIndexSeed, audit, priceIndexRepo);
    }

    // ── Create ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithPriceIndexItemId_SnapshotsNameUnitPrice()
    {
        Account acct = Acct(1);
        PriceIndexItem priceItem = PriceItem(10, "Bond paper", "ream", 250m);
        var (sut, _, _, _, _, _) = Build([acct], [priceItem]);

        UpsertProcurementPresetDto dto = new(1, "Office Supplies Kit", true,
            [new UpsertProcurementPresetItemDto(10, null, null, null, 4m)]);

        ServiceResult<ProcurementPresetDto> result = await sut.CreateAsync(Caller(), dto);

        Assert.True(result.IsSuccess);
        ProcurementPresetItemDto item = Assert.Single(result.Value!.Items);
        Assert.Equal("Bond paper", item.Name);
        Assert.Equal("ream", item.Unit);
        Assert.Equal(250m, item.UnitPrice);
        Assert.Equal(4m, item.DefaultQty);
        Assert.Equal(10, item.PriceIndexItemId);
    }

    [Fact]
    public async Task CreateAsync_LaterPriceIndexPriceChange_DoesNotAffectSnapshottedPreset()
    {
        Account acct = Acct(1);
        PriceIndexItem priceItem = PriceItem(10, "Bond paper", "ream", 250m);
        var (sut, _, _, priceIndexSeed, _, _) = Build([acct], [priceItem]);

        UpsertProcurementPresetDto dto = new(1, "Office Supplies Kit", true,
            [new UpsertProcurementPresetItemDto(10, null, null, null, 4m)]);

        ServiceResult<ProcurementPresetDto> created = await sut.CreateAsync(Caller(), dto);
        Assert.Equal(250m, created.Value!.Items[0].UnitPrice);

        // Simulate a later price-index update (RAL-118 config edit) — the source row's price changes.
        priceIndexSeed.Single(p => p.Id == 10).UnitPrice = 999m;

        ServiceResult<ProcurementPresetDto> refetched = await sut.GetByIdAsync(created.Value.Id);

        Assert.True(refetched.IsSuccess);
        Assert.Equal(250m, refetched.Value!.Items[0].UnitPrice); // unchanged — snapshot, not a live link
    }

    [Fact]
    public async Task CreateAsync_FreeTypedItem_NoPriceIndexItemId_UsesProvidedValues()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);

        UpsertProcurementPresetDto dto = new(1, "Custom Kit", true,
            [new UpsertProcurementPresetItemDto(null, "Whiteboard marker", "box", 120m, 2m)]);

        ServiceResult<ProcurementPresetDto> result = await sut.CreateAsync(Caller(), dto);

        Assert.True(result.IsSuccess);
        ProcurementPresetItemDto item = Assert.Single(result.Value!.Items);
        Assert.Null(item.PriceIndexItemId);
        Assert.Equal("Whiteboard marker", item.Name);
        Assert.Equal("box", item.Unit);
        Assert.Equal(120m, item.UnitPrice);
    }

    [Fact]
    public async Task CreateAsync_FreeTypedItem_MissingUnitPrice_ReturnsBadRequest()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);

        UpsertProcurementPresetDto dto = new(1, "Custom Kit", true,
            [new UpsertProcurementPresetItemDto(null, "Whiteboard marker", "box", null, 2m)]);

        ServiceResult<ProcurementPresetDto> result = await sut.CreateAsync(Caller(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_UnknownPriceIndexItemId_ReturnsBadRequest()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);

        UpsertProcurementPresetDto dto = new(1, "Kit", true,
            [new UpsertProcurementPresetItemDto(999, null, null, null, 1m)]);

        ServiceResult<ProcurementPresetDto> result = await sut.CreateAsync(Caller(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_UnknownAccountId_ReturnsBadRequest()
    {
        var (sut, _, _, _, _, _) = Build([], []);

        UpsertProcurementPresetDto dto = new(404, "Kit", true,
            [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]);

        ServiceResult<ProcurementPresetDto> result = await sut.CreateAsync(Caller(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_NoItems_ReturnsBadRequest()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);

        UpsertProcurementPresetDto dto = new(1, "Empty Kit", true, []);

        ServiceResult<ProcurementPresetDto> result = await sut.CreateAsync(Caller(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_StampsCallerAsCreatedBy()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);
        User caller = Caller();

        UpsertProcurementPresetDto dto = new(1, "Kit", true,
            [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]);

        ServiceResult<ProcurementPresetDto> result = await sut.CreateAsync(caller, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal(caller.Id, result.Value!.CreatedById);
    }

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, audit, _) = Build([acct], []);

        UpsertProcurementPresetDto dto = new(1, "Kit", true,
            [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]);

        await sut.CreateAsync(Caller(), dto);

        audit.Verify(a => a.LogAsync(
            "procurement_presets", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ReplacesItemSet_DeleteThenReinsert()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);

        ServiceResult<ProcurementPresetDto> created = await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            1, "Kit", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));
        int id = created.Value!.Id;

        ServiceResult<ProcurementPresetDto> updated = await sut.UpdateAsync(id, new UpsertProcurementPresetDto(
            1, "Kit v2", true,
            [
                new UpsertProcurementPresetItemDto(null, "Bond paper", "ream", 250m, 3m),
                new UpsertProcurementPresetItemDto(null, "Folder", "piece", 15m, 20m),
            ]));

        Assert.True(updated.IsSuccess);
        Assert.Equal("Kit v2", updated.Value!.Name);
        Assert.Equal(2, updated.Value.Items.Count);
        Assert.DoesNotContain(updated.Value.Items, i => i.Name == "Marker");
    }

    [Fact]
    public async Task UpdateAsync_DoesNotChangeCreatedBy()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);
        User caller = Caller();

        ServiceResult<ProcurementPresetDto> created = await sut.CreateAsync(caller, new UpsertProcurementPresetDto(
            1, "Kit", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));

        ServiceResult<ProcurementPresetDto> updated = await sut.UpdateAsync(created.Value!.Id, new UpsertProcurementPresetDto(
            1, "Kit v2", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));

        Assert.True(updated.IsSuccess);
        Assert.Equal(caller.Id, updated.Value!.CreatedById);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _) = Build([], []);

        ServiceResult<ProcurementPresetDto> result = await sut.UpdateAsync(999, new UpsertProcurementPresetDto(
            1, "Kit", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);

        ServiceResult<ProcurementPresetDto> created = await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            1, "Kit", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));

        ServiceResult<ProcurementPresetDto> deleted = await sut.DeleteAsync(created.Value!.Id);

        Assert.True(deleted.IsSuccess);
        Assert.False(deleted.Value!.IsActive);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _) = Build([], []);

        ServiceResult<ProcurementPresetDto> result = await sut.DeleteAsync(999);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Read: account scoping + active filter ────────────────────────────────

    [Fact]
    public async Task GetByAccountAsync_ScopesToAccount_ExcludesOtherAccounts()
    {
        Account acct1 = Acct(1);
        Account acct2 = Acct(2, "5-02-01-010", "Traveling Expenses");
        var (sut, _, _, _, _, _) = Build([acct1, acct2], []);

        await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            1, "Kit A", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));
        await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            2, "Kit B", true, [new UpsertProcurementPresetItemDto(null, "Ticket", "piece", 50m, 1m)]));

        IReadOnlyList<ProcurementPresetDto> result = await sut.GetByAccountAsync(1, ActiveFilter.All);

        ProcurementPresetDto only = Assert.Single(result);
        Assert.Equal("Kit A", only.Name);
    }

    [Fact]
    public async Task GetByAccountAsync_NullAccountId_ReturnsPresetsAcrossAllAccounts()
    {
        Account acct1 = Acct(1);
        Account acct2 = Acct(2, "5-02-01-010", "Traveling Expenses");
        var (sut, _, _, _, _, _) = Build([acct1, acct2], []);

        await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            1, "Kit A", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));
        await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            2, "Kit B", true, [new UpsertProcurementPresetItemDto(null, "Ticket", "piece", 50m, 1m)]));

        IReadOnlyList<ProcurementPresetDto> result = await sut.GetByAccountAsync(null, ActiveFilter.All);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, p => p.Name == "Kit A");
        Assert.Contains(result, p => p.Name == "Kit B");
    }

    [Fact]
    public async Task GetByAccountAsync_ActiveFilter_ExcludesDeactivated()
    {
        Account acct = Acct(1);
        var (sut, _, _, _, _, _) = Build([acct], []);

        ServiceResult<ProcurementPresetDto> created = await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            1, "Kit", true, [new UpsertProcurementPresetItemDto(null, "Marker", "box", 100m, 1m)]));
        await sut.DeleteAsync(created.Value!.Id);

        IReadOnlyList<ProcurementPresetDto> activeOnly = await sut.GetByAccountAsync(1, ActiveFilter.Active);
        IReadOnlyList<ProcurementPresetDto> all = await sut.GetByAccountAsync(1, ActiveFilter.All);

        Assert.Empty(activeOnly);
        Assert.Single(all);
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNotFound()
    {
        var (sut, _, _, _, _, _) = Build([], []);

        ServiceResult<ProcurementPresetDto> result = await sut.GetByIdAsync(999);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    // ── Scoped-query regression guards (RAL-164 — perf audit Tier 1) ──────────
    // Assert the by-ids scoped lookup is used, and the old full-table GetAllAsync()
    // is never called, on both call sites the audit flagged (validate + snapshot).

    [Fact]
    public async Task CreateAsync_UsesScopedPriceIndexLookup_NeverFullTableLoad()
    {
        Account acct = Acct(1);
        PriceIndexItem priceItem = PriceItem(10, "Bond paper", "ream", 250m);
        var (sut, _, _, _, _, priceIndexRepo) = Build([acct], [priceItem]);

        UpsertProcurementPresetDto dto = new(1, "Office Supplies Kit", true,
            [new UpsertProcurementPresetItemDto(10, null, null, null, 4m)]);

        await sut.CreateAsync(Caller(), dto);

        priceIndexRepo.Verify(r => r.GetByIdsAsync(
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids.Contains(10)), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        priceIndexRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateAsync_UsesScopedPriceIndexLookup_NeverFullTableLoad()
    {
        Account acct = Acct(1);
        PriceIndexItem priceItem = PriceItem(10, "Bond paper", "ream", 250m);
        var (sut, _, _, _, _, priceIndexRepo) = Build([acct], [priceItem]);

        ServiceResult<ProcurementPresetDto> created = await sut.CreateAsync(Caller(), new UpsertProcurementPresetDto(
            1, "Kit", true, [new UpsertProcurementPresetItemDto(10, null, null, null, 1m)]));
        priceIndexRepo.Invocations.Clear();

        await sut.UpdateAsync(created.Value!.Id, new UpsertProcurementPresetDto(
            1, "Kit", true, [new UpsertProcurementPresetItemDto(10, null, null, null, 2m)]));

        priceIndexRepo.Verify(r => r.GetByIdsAsync(
            It.Is<IReadOnlyList<int>>(ids => ids.Count == 1 && ids.Contains(10)), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        priceIndexRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_FreeTypedItemOnly_NeverQueriesPriceIndex()
    {
        // No item references a PriceIndexItemId — the scoped lookup should receive an empty
        // id list (or not be meaningfully invoked), never fall back to a full-table load.
        Account acct = Acct(1);
        var (sut, _, _, _, _, priceIndexRepo) = Build([acct], []);

        UpsertProcurementPresetDto dto = new(1, "Custom Kit", true,
            [new UpsertProcurementPresetItemDto(null, "Whiteboard marker", "box", 120m, 2m)]);

        await sut.CreateAsync(Caller(), dto);

        priceIndexRepo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
