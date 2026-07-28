using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Application.Services;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Tests.Application;

/// <summary>
/// Unit tests for <see cref="PriceIndexService"/> (v1.4 — RAL-118): CSV upsert by
/// (name, unit), price_updated_at auto-set on unit_price change, soft delete, and
/// audit log calls. CSV import is the PRIMARY real-world ingestion path (PPDO
/// uploads price lists downloaded from GSO's own application) so its per-row
/// error handling is exercised closely.
/// </summary>
public sealed class PriceIndexServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

    private static PriceIndexItem Item(
        int id, string name, string unit, decimal price, string? category = null,
        bool active = true, DateTime? priceUpdatedAt = null, bool daysEnabled = false,
        string? stockCardNo = null) => new()
    {
        Id = id, Name = name, Unit = unit, UnitPrice = price, Category = category,
        IsActive = active, DaysEnabled = daysEnabled, PriceUpdatedAt = priceUpdatedAt ?? FixedNow,
        StockCardNo = stockCardNo, CreatedAt = FixedNow, UpdatedAt = FixedNow,
    };

    private static (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) Build(
        List<PriceIndexItem> seed, IAuditService? audit = null)
    {
        Mock<IPriceIndexItemRepository> repo = new();
        repo.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(seed);
        // Lazy in-memory filter over the same live seed list, mirroring the real SQL-pushed
        // WHERE (RAL-166 follow-up) — mocks GetFilteredAsync rather than the old GetAllAsync()
        // full-table read so Create/Update-then-Get flows still see new rows.
        repo.Setup(r => r.GetFilteredAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((bool? isActive, string? search, CancellationToken _) =>
            {
                IEnumerable<PriceIndexItem> q = seed;
                if (isActive.HasValue) q = q.Where(p => p.IsActive == isActive.Value);
                if (!string.IsNullOrWhiteSpace(search))
                {
                    string s = search.Trim();
                    q = q.Where(p =>
                        p.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                        (p.Category != null && p.Category.Contains(s, StringComparison.OrdinalIgnoreCase)) ||
                        (p.StockCardNo != null && p.StockCardNo.Contains(s, StringComparison.OrdinalIgnoreCase)));
                }
                return (IReadOnlyList<PriceIndexItem>)q.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            });
        repo.Setup(r => r.AddAsync(It.IsAny<PriceIndexItem>(), It.IsAny<CancellationToken>()))
            .Callback<PriceIndexItem, CancellationToken>((p, _) => seed.Add(p))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PriceIndexItem>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new PriceIndexService(repo.Object, NullLogger<PriceIndexService>.Instance,
            audit ?? Mock.Of<IAuditService>()), repo);
    }

    private static (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo, Mock<IAuditService> audit)
        BuildWithAudit(List<PriceIndexItem> seed)
    {
        Mock<IAuditService> audit = new();
        audit.Setup(a => a.LogAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<string>(),
            It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed, audit.Object);
        return (sut, repo, audit);
    }

    // ── CRUD ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_DuplicateNameAndUnit_ReturnsConflict()
    {
        (PriceIndexService sut, _) = Build([Item(1, "Bond Paper", "ream", 250m)]);
        ServiceResult<PriceIndexItemDto> result =
            await sut.CreateAsync(new UpsertPriceIndexItemDto("Bond Paper", "ream", 300m, null));
        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task CreateAsync_SameNameDifferentUnit_Succeeds()
    {
        (PriceIndexService sut, _) = Build([Item(1, "Bond Paper", "ream", 250m)]);
        ServiceResult<PriceIndexItemDto> result =
            await sut.CreateAsync(new UpsertPriceIndexItemDto("Bond Paper", "box", 2500m, null));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task CreateAsync_New_ReturnsOk()
    {
        (PriceIndexService sut, _) = Build([]);
        ServiceResult<PriceIndexItemDto> result =
            await sut.CreateAsync(new UpsertPriceIndexItemDto("Ballpen", "piece", 15.50m, "Office Supplies"));
        Assert.True(result.IsSuccess);
        Assert.Equal("Ballpen", result.Value!.Name);
        Assert.Equal(15.50m, result.Value.UnitPrice);
        Assert.Equal("Office Supplies", result.Value.Category);
    }

    [Fact]
    public async Task CreateAsync_NegativePrice_ReturnsBadRequest()
    {
        (PriceIndexService sut, _) = Build([]);
        ServiceResult<PriceIndexItemDto> result =
            await sut.CreateAsync(new UpsertPriceIndexItemDto("Ballpen", "piece", -1m, null));
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_MissingName_ReturnsBadRequest()
    {
        (PriceIndexService sut, _) = Build([]);
        ServiceResult<PriceIndexItemDto> result =
            await sut.CreateAsync(new UpsertPriceIndexItemDto("  ", "piece", 15m, null));
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task CreateAsync_DaysEnabledTrue_RoundTrips()
    {
        (PriceIndexService sut, _) = Build([]);
        ServiceResult<PriceIndexItemDto> result = await sut.CreateAsync(
            new UpsertPriceIndexItemDto("Venue Rental", "day", 5000m, "Venue", DaysEnabled: true));
        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DaysEnabled);
    }

    [Fact]
    public async Task UpdateAsync_TogglesDaysEnabled()
    {
        PriceIndexItem target = Item(1, "Venue Rental", "day", 5000m, daysEnabled: false);
        (PriceIndexService sut, _) = Build([target]);

        ServiceResult<PriceIndexItemDto> result = await sut.UpdateAsync(
            1, new UpsertPriceIndexItemDto("Venue Rental", "day", 5000m, null, DaysEnabled: true));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.DaysEnabled);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes()
    {
        PriceIndexItem target = Item(1, "Ballpen", "piece", 15m);
        (PriceIndexService sut, _) = Build([target]);
        ServiceResult<PriceIndexItemDto> result = await sut.DeleteAsync(1);
        Assert.True(result.IsSuccess);
        Assert.False(target.IsActive);
    }

    // ── price_updated_at behavior (the core new rule) ───────────────────────

    [Fact]
    public async Task CreateAsync_SetsPriceUpdatedAt()
    {
        (PriceIndexService sut, _) = Build([]);
        ServiceResult<PriceIndexItemDto> result =
            await sut.CreateAsync(new UpsertPriceIndexItemDto("Ballpen", "piece", 15m, null));
        Assert.True(result.IsSuccess);
        Assert.True((DateTime.UtcNow - result.Value!.PriceUpdatedAt).TotalSeconds < 5);
    }

    [Fact]
    public async Task UpdateAsync_PriceChanged_BumpsPriceUpdatedAt()
    {
        PriceIndexItem target = Item(1, "Ballpen", "piece", 15m, priceUpdatedAt: FixedNow);
        (PriceIndexService sut, _) = Build([target]);

        ServiceResult<PriceIndexItemDto> result =
            await sut.UpdateAsync(1, new UpsertPriceIndexItemDto("Ballpen", "piece", 18m, null));

        Assert.True(result.IsSuccess);
        Assert.Equal(18m, result.Value!.UnitPrice);
        Assert.True(result.Value.PriceUpdatedAt > FixedNow);
    }

    [Fact]
    public async Task UpdateAsync_PriceUnchanged_DoesNotBumpPriceUpdatedAt()
    {
        PriceIndexItem target = Item(1, "Ballpen", "piece", 15m, priceUpdatedAt: FixedNow);
        (PriceIndexService sut, _) = Build([target]);

        ServiceResult<PriceIndexItemDto> result = await sut.UpdateAsync(
            1, new UpsertPriceIndexItemDto("Ballpen", "piece", 15m, "Office Supplies"));

        Assert.True(result.IsSuccess);
        Assert.Equal(FixedNow, result.Value!.PriceUpdatedAt);
        Assert.Equal("Office Supplies", result.Value.Category); // other fields still update
    }

    // ── CSV import — the primary real-world workflow ────────────────────────

    [Fact]
    public async Task ImportCsvAsync_UpsertByNameAndUnit_CountsNewUpdatedSkipped()
    {
        List<PriceIndexItem> seed =
        [
            Item(1, "Bond Paper", "ream", 250m, priceUpdatedAt: FixedNow),
            Item(2, "Ballpen", "piece", 15m, priceUpdatedAt: FixedNow),
        ];
        (PriceIndexService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active",
            "Bond Paper,ream,250,,true",                    // unchanged -> skipped
            "Ballpen,piece,18,Office Supplies,true",         // price + category changed -> updated
            "Folder,piece,5.50,Office Supplies,true");       // new -> inserted

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(1, result.Value.Updated);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Empty(result.Value.Errors);
    }

    [Fact]
    public async Task ImportCsvAsync_PriceChangeInRow_BumpsPriceUpdatedAt()
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m, priceUpdatedAt: FixedNow)];
        (PriceIndexService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active",
            "Ballpen,piece,20,,true");

        await sut.ImportCsvAsync(csv);

        Assert.Equal(20m, seed.Single().UnitPrice);
        Assert.True(seed.Single().PriceUpdatedAt > FixedNow);
    }

    [Fact]
    public async Task ImportCsvAsync_MissingRequiredFields_SkipsWithRowLevelError()
    {
        (PriceIndexService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active",
            ",piece,15,,true",           // missing name
            "Ballpen,,15,,true");        // missing unit

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(0, result.Value!.New);
        Assert.Equal(2, result.Value.Skipped);
        Assert.Equal(2, result.Value.Errors.Count);
        Assert.Contains("Row 2", result.Value.Errors[0]);
        Assert.Contains("Row 3", result.Value.Errors[1]);
    }

    [Fact]
    public async Task ImportCsvAsync_MalformedPrice_SkipsWithRowLevelError_DoesNotThrow()
    {
        (PriceIndexService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active",
            "Ballpen,piece,not-a-number,,true");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Contains("Row 2", result.Value.Errors[0]);
        Assert.Contains("unit_price", result.Value.Errors[0]);
    }

    [Fact]
    public async Task ImportCsvAsync_NegativePrice_SkipsWithRowLevelError()
    {
        (PriceIndexService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active",
            "Ballpen,piece,-5,,true");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(0, result.Value!.New);
        Assert.Equal(1, result.Value.Skipped);
        Assert.Contains("Row 2", result.Value.Errors[0]);
    }

    [Fact]
    public async Task ImportCsvAsync_EmptyFile_ReturnsBadRequest()
    {
        (PriceIndexService sut, _) = Build([]);
        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync("");
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task ExportCsvAsync_IncludesExpectedColumns()
    {
        (PriceIndexService sut, _) = Build([Item(1, "Ballpen", "piece", 15m, "Office Supplies")]);
        string csv = await sut.ExportCsvAsync();
        Assert.Contains("name", csv);
        Assert.Contains("unit_price", csv);
        Assert.Contains("Ballpen", csv);
        Assert.Contains("Office Supplies", csv);
        Assert.Contains("days_enabled", csv);
    }

    [Fact]
    public async Task ImportCsvAsync_DaysEnabledColumn_RoundTrips()
    {
        (PriceIndexService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active,days_enabled",
            "Venue Rental,day,5000,Venue,true,true");

        await sut.ImportCsvAsync(csv);

        IReadOnlyList<PriceIndexItemDto> all = await sut.GetAllAsync(null, ActiveFilter.All);
        Assert.True(all.Single(p => p.Name == "Venue Rental").DaysEnabled);
    }

    [Fact]
    public async Task ImportCsvAsync_DaysEnabledChangedOnly_CountsAsUpdated()
    {
        List<PriceIndexItem> seed = [Item(1, "Venue Rental", "day", 5000m, "Venue", daysEnabled: false)];
        (PriceIndexService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active,days_enabled",
            "Venue Rental,day,5000,Venue,true,true");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);
        Assert.True(seed.Single().DaysEnabled);
    }

    // ── stock card no. (v1.5 — PPMP report) ──────────────────────────────────

    [Fact]
    public async Task CreateAsync_WithStockCardNo_PersistsIt()
    {
        (PriceIndexService sut, _) = Build([]);

        ServiceResult<PriceIndexItemDto> result = await sut.CreateAsync(
            new UpsertPriceIndexItemDto("Bond paper A4 80gsm", "ream", 494m, "Paper",
                StockCardNo: "OS-PAP-0000004"));

        Assert.Equal("OS-PAP-0000004", result.Value!.StockCardNo);
    }

    [Fact]
    public async Task CreateAsync_BlankStockCardNo_StoredAsNull()
    {
        (PriceIndexService sut, _) = Build([]);

        ServiceResult<PriceIndexItemDto> result = await sut.CreateAsync(
            new UpsertPriceIndexItemDto("Ballpen", "piece", 9m, null, StockCardNo: "   "));

        Assert.Null(result.Value!.StockCardNo);
    }

    [Fact]
    public async Task UpdateAsync_ChangesStockCardNo()
    {
        List<PriceIndexItem> seed = [Item(1, "Bond paper A4 80gsm", "ream", 494m, "Paper", stockCardNo: "OS-PAP-0000001")];
        (PriceIndexService sut, _) = Build(seed);

        await sut.UpdateAsync(1, new UpsertPriceIndexItemDto(
            "Bond paper A4 80gsm", "ream", 494m, "Paper", StockCardNo: "OS-PAP-0000004"));

        Assert.Equal("OS-PAP-0000004", seed.Single().StockCardNo);
    }

    [Fact]
    public async Task ExportCsvAsync_IncludesStockCardNo()
    {
        (PriceIndexService sut, _) = Build(
            [Item(1, "Bond paper A4 80gsm", "ream", 494m, "Paper", stockCardNo: "OS-PAP-0000004")]);

        string csv = await sut.ExportCsvAsync();

        Assert.Contains("stock_card_no", csv);
        Assert.Contains("OS-PAP-0000004", csv);
    }

    [Fact]
    public async Task ImportCsvAsync_StockCardNoColumn_RoundTrips()
    {
        (PriceIndexService sut, _) = Build([]);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active,days_enabled,stock_card_no",
            "Bond paper A4 80gsm,ream,494,Paper,true,false,OS-PAP-0000004");

        await sut.ImportCsvAsync(csv);

        IReadOnlyList<PriceIndexItemDto> all = await sut.GetAllAsync(null, ActiveFilter.All);
        Assert.Equal("OS-PAP-0000004", all.Single().StockCardNo);
    }

    [Fact]
    public async Task ImportCsvAsync_StockCardNoChangedOnly_CountsAsUpdated()
    {
        List<PriceIndexItem> seed = [Item(1, "Bond paper A4 80gsm", "ream", 494m, "Paper", stockCardNo: "OS-PAP-0000001")];
        (PriceIndexService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active,days_enabled,stock_card_no",
            "Bond paper A4 80gsm,ream,494,Paper,true,false,OS-PAP-0000004");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal("OS-PAP-0000004", seed.Single().StockCardNo);
    }

    /// <summary>
    /// The backward-compatibility rule: a CSV exported before stock_card_no existed has only
    /// six columns. That absent column must mean "leave alone", NOT "clear" — otherwise
    /// re-importing an older price list silently wipes every stock card number on file.
    /// </summary>
    [Fact]
    public async Task ImportCsvAsync_LegacySixColumnFile_PreservesExistingStockCardNo()
    {
        List<PriceIndexItem> seed = [Item(1, "Bond paper A4 80gsm", "ream", 494m, "Paper", stockCardNo: "OS-PAP-0000004")];
        (PriceIndexService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active,days_enabled",
            "Bond paper A4 80gsm,ream,520,Paper,true,false");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);       // the price change still applies
        Assert.Equal(520m, seed.Single().UnitPrice);
        Assert.Equal("OS-PAP-0000004", seed.Single().StockCardNo);
    }

    /// <summary>
    /// The other half of that rule: a row that HAS the column but leaves it blank does clear
    /// it — matching how category already behaves, so a value can still be removed via CSV.
    /// </summary>
    [Fact]
    public async Task ImportCsvAsync_BlankStockCardNoInPresentColumn_ClearsIt()
    {
        List<PriceIndexItem> seed = [Item(1, "Bond paper A4 80gsm", "ream", 494m, "Paper", stockCardNo: "OS-PAP-0000004")];
        (PriceIndexService sut, _) = Build(seed);

        string csv = string.Join("\r\n",
            "name,unit,unit_price,category,is_active,days_enabled,stock_card_no",
            "Bond paper A4 80gsm,ream,494,Paper,true,false,");

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Updated);
        Assert.Null(seed.Single().StockCardNo);
    }

    [Fact]
    public async Task GetAllAsync_SearchByStockCardNo_MatchesItem()
    {
        (PriceIndexService sut, _) = Build([
            Item(1, "Bond paper A4 80gsm", "ream", 494m, "Paper", stockCardNo: "OS-PAP-0000004"),
            Item(2, "Ballpen", "piece", 9m, "Pen", stockCardNo: "OS-PEN-0000015"),
        ]);

        IReadOnlyList<PriceIndexItemDto> found = await sut.GetAllAsync("OS-PEN", ActiveFilter.All);

        Assert.Equal("Ballpen", Assert.Single(found).Name);
    }

    // ── search ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAllAsync_SearchMatchesNameOrCategory()
    {
        List<PriceIndexItem> seed =
        [
            Item(1, "Bond Paper", "ream", 250m, "Office Supplies"),
            Item(2, "Diesel", "liter", 60m, "Fuel"),
        ];
        (PriceIndexService sut, _) = Build(seed);

        IReadOnlyList<PriceIndexItemDto> byName = await sut.GetAllAsync("bond", ActiveFilter.All);
        Assert.Single(byName);

        IReadOnlyList<PriceIndexItemDto> byCategory = await sut.GetAllAsync("fuel", ActiveFilter.All);
        Assert.Single(byCategory);
    }

    [Fact]
    public async Task GetAllAsync_ActiveFilter_ExcludesInactiveByDefault()
    {
        List<PriceIndexItem> seed =
        [
            Item(1, "Bond Paper", "ream", 250m, active: true),
            Item(2, "Old Item", "piece", 5m, active: false),
        ];
        (PriceIndexService sut, _) = Build(seed);

        IReadOnlyList<PriceIndexItemDto> active = await sut.GetAllAsync(null, ActiveFilter.Active);
        Assert.Single(active);
        Assert.Equal("Bond Paper", active[0].Name);
    }

    [Fact]
    public async Task GetAllAsync_UsesScopedQuery_NeverFullTableLoad()
    {
        // RAL-166 follow-up: the catalogue runs to ~6,400 rows in practice — GetAllAsync must
        // push the active/search filter to SQL via GetFilteredAsync, never materialize+filter
        // the whole table in memory.
        List<PriceIndexItem> seed = [Item(1, "Bond Paper", "ream", 250m)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed);

        await sut.GetAllAsync("bond", ActiveFilter.Active);

        repo.Verify(r => r.GetFilteredAsync(true, "bond", It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── audit logging ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAsync_CallsAuditLog_WithCreateAction()
    {
        (PriceIndexService sut, _, Mock<IAuditService> audit) = BuildWithAudit([]);

        await sut.CreateAsync(new UpsertPriceIndexItemDto("Ballpen", "piece", 15m, null));

        audit.Verify(a => a.LogAsync(
            "price_index_items", It.IsAny<int>(), AuditAction.Create,
            null, It.IsAny<object?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_CallsAuditLog_CapturingOldAndNewValues()
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m)];
        (PriceIndexService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.UpdateAsync(1, new UpsertPriceIndexItemDto("Ballpen", "piece", 18m, null));

        audit.Verify(a => a.LogAsync(
            "price_index_items", 1, AuditAction.Update,
            It.IsNotNull<object>(), It.IsNotNull<object>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteAsync_CallsAuditLog_WithDeleteAction()
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m)];
        (PriceIndexService sut, _, Mock<IAuditService> audit) = BuildWithAudit(seed);

        await sut.DeleteAsync(1);

        audit.Verify(a => a.LogAsync(
            "price_index_items", 1, AuditAction.Delete,
            It.IsNotNull<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
