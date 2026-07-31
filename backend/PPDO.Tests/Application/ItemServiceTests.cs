using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Items;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="ItemService"/> — written first (TDD).
/// IItemMasterRepository and IPermissionService are mocked.
/// Coverage target: 80% (Application/Service layer).
/// </summary>
public sealed class ItemServiceTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private static User MakeAdmin() => new()
    {
        Id = Guid.NewGuid(), FullName = "Admin", Email = "admin@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Admin, DivisionId = null, IsActive = true,
    };

    private static User MakeStaffWithInventory() => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff", Email = "staff@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, DivisionId = 2,
        Division = new Division { Id = 2, OfficeId = 100, Name = "Planning Division", CanAccessInventory = true },
        IsActive = true,
    };

    private static User MakeStaffNoInventory() => new()
    {
        Id = Guid.NewGuid(), FullName = "Staff2", Email = "staff2@ppdo.gov.ph",
        PasswordHash = "hash", Role = UserRole.Staff, DivisionId = 2,
        Division = new Division { Id = 2, OfficeId = 100, Name = "Planning Division", CanAccessInventory = false },
        IsActive = true,
    };

    private static ItemMaster MakeItem(string stockNo = "01-01-01-01") => new()
    {
        Id = Guid.NewGuid(), StockNo = stockNo, Description = "Bond Paper A4",
        Unit = "ream", UnitCost = 220m, Category = "Office Supplies",
        IsNewItem = false, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Mock<IItemMasterRepository> RepoThatSaves()
    {
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        return repo;
    }

    private static ItemService BuildSut(Mock<IItemMasterRepository> repo) =>
        new(repo.Object, new PermissionService(), NullLogger<ItemService>.Instance);

    // ── GetAllAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_ReturnsMappedDtos()
    {
        List<ItemMaster> items = [MakeItem("01-01-01-01"), MakeItem("01-01-01-02")];
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        IReadOnlyList<ItemMasterDto> result = await BuildSut(repo).GetAllAsync();

        Assert.Equal(2, result.Count);
    }

    // ── GetByIdAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetByIdAsync_NotFound_ReturnsNotFound()
    {
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        ServiceResult<ItemMasterDto> result = await BuildSut(repo).GetByIdAsync(Guid.NewGuid());

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task GetByIdAsync_Found_ReturnsOk()
    {
        ItemMaster item = MakeItem();
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        ServiceResult<ItemMasterDto> result = await BuildSut(repo).GetByIdAsync(item.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(item.StockNo, result.Value!.StockNo);
    }

    // ── LookupAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ReturnsMappedDtos()
    {
        List<ItemMaster> items = [MakeItem("01-01")];
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.SearchAsync("01-01", It.IsAny<CancellationToken>()))
            .ReturnsAsync(items);

        IReadOnlyList<ItemLookupDto> result = await BuildSut(repo).LookupAsync("01-01");

        Assert.Single(result);
        Assert.Equal("01-01", result[0].StockNo);
    }

    [Fact]
    public async Task LookupAsync_EmptyTerm_ReturnsEmpty()
    {
        Mock<IItemMasterRepository> repo = new();

        IReadOnlyList<ItemLookupDto> result = await BuildSut(repo).LookupAsync("  ");

        Assert.Empty(result);
        repo.Verify(r => r.SearchAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── CreateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithoutInventoryPermission_ReturnsForbidden()
    {
        Mock<IItemMasterRepository> repo = new();
        CreateItemMasterDto dto = new("01-01-01-99", "New Item", "pcs", 10m, null, null, 0, null, false);

        ServiceResult<ItemMasterDto> result =
            await BuildSut(repo).CreateAsync(MakeStaffNoInventory(), dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task CreateAsync_DuplicateStockNo_ReturnsConflict()
    {
        ItemMaster existing = MakeItem("01-01-01-99");
        Mock<IItemMasterRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByStockNoAsync("01-01-01-99", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        CreateItemMasterDto dto = new("01-01-01-99", "Dup", "pcs", 10m, null, null, 0, null, false);

        ServiceResult<ItemMasterDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_EmptyStockNo_ReturnsBadRequest()
    {
        Mock<IItemMasterRepository> repo = new();
        CreateItemMasterDto dto = new("", "Item", "pcs", 10m, null, null, 0, null, false);

        ServiceResult<ItemMasterDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_EmptyDescription_ReturnsBadRequest()
    {
        Mock<IItemMasterRepository> repo = new();
        CreateItemMasterDto dto = new("01-99", "", "pcs", 10m, null, null, 0, null, false);

        ServiceResult<ItemMasterDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_ValidDto_ReturnsOk()
    {
        Mock<IItemMasterRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByStockNoAsync("NEW-001", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        CreateItemMasterDto dto = new("NEW-001", "New Item", "pcs", 10m, "Office", "Supplies", 5, null, false);

        ServiceResult<ItemMasterDto> result = await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("NEW-001", result.Value!.StockNo);
    }

    [Fact]
    public async Task CreateAsync_IsNewItem_FlagPreserved()
    {
        ItemMaster? saved = null;
        Mock<IItemMasterRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByStockNoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);
        repo.Setup(r => r.AddAsync(It.IsAny<ItemMaster>(), It.IsAny<CancellationToken>()))
            .Callback<ItemMaster, CancellationToken>((e, _) => saved = e)
            .Returns(Task.CompletedTask);

        CreateItemMasterDto dto = new("NEW-002", "Unreviewed", "pcs", 5m, null, null, 0, null, true);
        await BuildSut(repo).CreateAsync(MakeAdmin(), dto);

        Assert.NotNull(saved);
        Assert.True(saved!.IsNewItem);
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_WithoutInventoryPermission_ReturnsForbidden()
    {
        ItemMaster item = MakeItem();
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        UpdateItemMasterDto dto = new(null, "Updated", "pcs", 10m, null, null, 0, null, false);

        ServiceResult<ItemMasterDto> result =
            await BuildSut(repo).UpdateAsync(MakeStaffNoInventory(), item.Id, dto);

        Assert.Equal(ServiceErrorCode.Forbidden, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_NotFound_ReturnsNotFound()
    {
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ItemMaster?)null);

        UpdateItemMasterDto dto = new(null, "Updated", "pcs", 10m, null, null, 0, null, false);

        ServiceResult<ItemMasterDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), Guid.NewGuid(), dto);

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_ValidDto_SavesFields()
    {
        ItemMaster item = MakeItem();
        Mock<IItemMasterRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        UpdateItemMasterDto dto = new(null, "Updated Description", "box", 50m, "Supplies", "Office", 10, "Updated remark", false);

        ServiceResult<ItemMasterDto> result =
            await BuildSut(repo).UpdateAsync(MakeAdmin(), item.Id, dto);

        Assert.True(result.IsSuccess);
        Assert.Equal("Updated Description", item.Description);
        Assert.Equal("box", item.Unit);
        Assert.Equal(50m, item.UnitCost);
    }

    [Fact]
    public async Task UpdateAsync_ClearsIsNewItemFlag()
    {
        ItemMaster item = MakeItem();
        item.IsNewItem = true;

        Mock<IItemMasterRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        UpdateItemMasterDto dto = new("01-01-01-01", "Bond Paper A4", "ream", 220m, "Office Supplies", null, 0, null, false);

        await BuildSut(repo).UpdateAsync(MakeAdmin(), item.Id, dto);

        Assert.False(item.IsNewItem);
    }

    [Fact]
    public async Task UpdateAsync_StaffWithInventory_CanUpdate()
    {
        ItemMaster item = MakeItem();
        Mock<IItemMasterRepository> repo = RepoThatSaves();
        repo.Setup(r => r.GetByIdAsync(item.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        UpdateItemMasterDto dto = new(null, "Updated", "ream", 200m, null, null, 0, null, false);

        ServiceResult<ItemMasterDto> result =
            await BuildSut(repo).UpdateAsync(MakeStaffWithInventory(), item.Id, dto);

        Assert.True(result.IsSuccess);
    }

    // ── SearchAsync (RAL-192 — server-side pagination) ──────────────────────────

    private static ItemMasterSearchFilterDto DefaultFilter(int page = 1, int pageSize = 25) =>
        new(page, pageSize, Search: null, StockNo: null, Description: null, Category: null,
            Unit: null, ItemType: null, Remarks: null, IsNewOnly: false, SortBy: null,
            SortDescending: false);

    [Fact]
    public async Task SearchAsync_BuildsCriteriaFromFilterAndMapsResult()
    {
        List<ItemMaster> items = [MakeItem("01-01-01-01"), MakeItem("01-01-01-02")];
        Mock<IItemMasterRepository> repo = new();
        ItemMasterSearchCriteria? captured = null;
        repo.Setup(r => r.SearchAsync(It.IsAny<ItemMasterSearchCriteria>(), It.IsAny<CancellationToken>()))
            .Callback<ItemMasterSearchCriteria, CancellationToken>((c, _) => captured = c)
            .ReturnsAsync(new ItemMasterSearchResult(items, TotalCount: 2, TotalNewItemCount: 1));

        ItemMasterSearchFilterDto filter = DefaultFilter(page: 2, pageSize: 10) with
        {
            Search = "bond", IsNewOnly = true, SortBy = "description", SortDescending = true,
        };

        ItemMasterSearchResultDto result = await BuildSut(repo).SearchAsync(filter);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(2, result.TotalCount);
        Assert.Equal(1, result.TotalNewItemCount);
        Assert.Equal(2, result.Page);
        Assert.Equal(10, result.PageSize);

        Assert.NotNull(captured);
        Assert.Equal("bond", captured!.Search);
        Assert.True(captured.IsNewOnly);
        Assert.Equal("description", captured.SortBy);
        Assert.True(captured.SortDescending);
        Assert.Equal(2, captured.Page);
        Assert.Equal(10, captured.PageSize);
    }

    [Fact]
    public async Task SearchAsync_NeverLoadsTheFullCatalog()
    {
        // Regression guard — the page must go through SearchAsync (WHERE + COUNT + OFFSET/FETCH
        // in SQL), never the plain GetAllAsync() base-repository call.
        Mock<IItemMasterRepository> repo = new();
        repo.Setup(r => r.SearchAsync(It.IsAny<ItemMasterSearchCriteria>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ItemMasterSearchResult([], TotalCount: 0, TotalNewItemCount: 0));

        await BuildSut(repo).SearchAsync(DefaultFilter());

        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
