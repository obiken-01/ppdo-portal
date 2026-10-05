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
        List<PriceIndexItem> seed, IAuditService? audit = null, IExcelService? excel = null)
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
        // RAL-232 — count and picker are separate SQL reads, not filters over the full list.
        // Mocked off the same seed so a service-side filter-translation bug still shows up.
        repo.Setup(r => r.CountAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((bool? isActive, string? _, CancellationToken __) =>
                seed.Count(p => !isActive.HasValue || p.IsActive == isActive.Value));
        repo.Setup(r => r.GetPickerItemsAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((bool? isActive, string? _, CancellationToken __) =>
                (IReadOnlyList<PriceIndexPickerItem>)seed
                    .Where(p => !isActive.HasValue || p.IsActive == isActive.Value)
                    .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(p => new PriceIndexPickerItem(p.Id, p.Name, p.Unit, p.UnitPrice, p.DaysEnabled))
                    .ToList());
        // RAL-233 — mocks the same whitelist ApplySort implements in SQL, so a service-side
        // mismatch between the two (e.g. a typo'd column key) still shows up here.
        repo.Setup(r => r.GetPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((bool? isActive, string? search, string? sortColumn, bool descending,
                int page, int pageSize, CancellationToken _) =>
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
                Func<PriceIndexItem, IComparable> key = sortColumn?.ToLowerInvariant() switch
                {
                    "unit"           => p => p.Unit,
                    "stockcardno"    => p => p.StockCardNo ?? "",
                    "category"       => p => p.Category ?? "",
                    "unitprice"      => p => p.UnitPrice,
                    "priceupdatedat" => p => p.PriceUpdatedAt,
                    "daysenabled"    => p => p.DaysEnabled,
                    "isactive"       => p => p.IsActive,
                    _                => p => p.Name,
                };
                List<PriceIndexItem> ordered = (descending ? q.OrderByDescending(key) : q.OrderBy(key)).ToList();
                return ((IReadOnlyList<PriceIndexItem>)ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                        ordered.Count);
            });
        // PPDO-186 — the single-item paths read one row and ask SQL about duplicates. Both mocked
        // off the live seed; the duplicate check mirrors the case-insensitive DB collation.
        repo.Setup(r => r.GetByIntIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => seed.FirstOrDefault(p => p.Id == id));
        repo.Setup(r => r.NameAndUnitExistsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string name, string unit, int? excludeId, CancellationToken _) =>
                seed.Any(p => (excludeId == null || p.Id != excludeId.Value)
                           && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                           && p.Unit.Equals(unit, StringComparison.OrdinalIgnoreCase)));
        repo.Setup(r => r.AddAsync(It.IsAny<PriceIndexItem>(), It.IsAny<CancellationToken>()))
            .Callback<PriceIndexItem, CancellationToken>((p, _) => seed.Add(p))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateAsync(It.IsAny<PriceIndexItem>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        return (new PriceIndexService(repo.Object, NullLogger<PriceIndexService>.Instance,
            audit ?? Mock.Of<IAuditService>(), excel ?? Mock.Of<IExcelService>()), repo);
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

    // ── Single-item reads and saves stay off the full catalogue (PPDO-186) ────

    [Fact]
    public async Task UpdateAsync_RenameOntoAnotherItemsNameAndUnit_ReturnsConflict()
    {
        (PriceIndexService sut, _) = Build(
            [Item(1, "Bond Paper", "ream", 250m), Item(2, "Ballpen", "piece", 15m)]);

        ServiceResult<PriceIndexItemDto> result =
            await sut.UpdateAsync(2, new UpsertPriceIndexItemDto("Bond Paper", "ream", 15m, null));

        Assert.Equal(ServiceErrorCode.Conflict, result.Code);
    }

    [Fact]
    public async Task UpdateAsync_KeepingItsOwnNameAndUnit_IsNotADuplicate()
    {
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build([Item(1, "Bond Paper", "ream", 250m)]);

        ServiceResult<PriceIndexItemDto> result =
            await sut.UpdateAsync(1, new UpsertPriceIndexItemDto("Bond Paper", "ream", 275m, null));

        Assert.True(result.IsSuccess);
        repo.Verify(r => r.NameAndUnitExistsAsync("Bond Paper", "ream", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_PaddedNameAndUnit_ChecksTheTrimmedKeyWithNoExclusion()
    {
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build([]);

        await sut.CreateAsync(new UpsertPriceIndexItemDto("  Bond Paper ", " ream ", 250m, null));

        repo.Verify(r => r.NameAndUnitExistsAsync("Bond Paper", "ream", null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_ReturnsNotFound()
    {
        (PriceIndexService sut, _) = Build([Item(1, "Bond Paper", "ream", 250m)]);

        ServiceResult<PriceIndexItemDto> result =
            await sut.UpdateAsync(99, new UpsertPriceIndexItemDto("Bond Paper", "ream", 250m, null));

        Assert.Equal(ServiceErrorCode.NotFound, result.Code);
    }

    [Fact]
    public async Task SingleItemPaths_NeverLoadTheWholeCatalogue()
    {
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build([Item(1, "Bond Paper", "ream", 250m)]);

        await sut.GetByIdAsync(1);
        await sut.CreateAsync(new UpsertPriceIndexItemDto("Ballpen", "piece", 15m, null));
        await sut.UpdateAsync(1, new UpsertPriceIndexItemDto("Bond Paper", "ream", 260m, null));
        await sut.DeleteAsync(1);

        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
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

    // ── PGOM workbook import ──────────────────────────────────────────────────

    private static PriceIndexImportRow Pgom(
        int row, string code, string description, string account, string unit, decimal? price, string? error = null) =>
        new()
        {
            RowNumber = row, ItemCode = code, Description = description,
            AccountName = account, Unit = unit, Price = price, Error = error,
        };

    private static (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo, List<PriceIndexItem> seed) BuildPgom(
        IReadOnlyList<PriceIndexImportRow> rows, List<PriceIndexItem>? seed = null)
    {
        seed ??= [];
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParsePriceIndexImport(It.IsAny<Stream>())).Returns(rows);
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed, excel: excel.Object);
        return (sut, repo, seed);
    }

    [Fact]
    public async Task ImportPgomAsync_MapsColumns_AndMergesDuplicateNameUnit_LastRowWins()
    {
        (PriceIndexService sut, _, List<PriceIndexItem> seed) = BuildPgom(
        [
            Pgom(2, "OSAMFD-1", "Garden Hose (20m)", "Other Supplies", "roll", 6553m),
            Pgom(3, "CMFD-2",   "Garden Hose (20m)", "Construction Materials", "ROLL", 6600.456m),
            Pgom(4, "X-3",      "Bond paper",        "Office Supplies", "ream", 250m),
        ]);

        ServiceResult<CsvImportResult> result = await sut.ImportPgomAsync(new MemoryStream());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value!.New);
        Assert.Equal(2, seed.Count);
        PriceIndexItem hose = seed.Single(p => p.Name == "Garden Hose (20m)");
        Assert.Equal("CMFD-2", hose.StockCardNo);
        Assert.Equal("Construction Materials", hose.Category);
        Assert.Equal(6600.46m, hose.UnitPrice);
        Assert.True(hose.IsActive);
        Assert.Contains(result.Value.Notes!, n => n.Contains("1 rows") && n.Contains("merged"));
        Assert.Empty(result.Value.Errors);
    }

    [Fact]
    public async Task ImportPgomAsync_CleansLineBreaksAndExcelEscapes_FromName()
    {
        (PriceIndexService sut, _, List<PriceIndexItem> seed) = BuildPgom(
        [
            Pgom(2, "NFE-1", "Certificate of Marriage_x000d__x000d_\nRev. 2016", "Forms", "pad", 240m),
            Pgom(3, "T-2", "Trauma Bag \t\t\n\tCapacity: 26L", "Medical", "pc", 100m),
        ]);

        await sut.ImportPgomAsync(new MemoryStream());

        Assert.Contains(seed, p => p.Name == "Certificate of Marriage Rev. 2016");
        Assert.Contains(seed, p => p.Name == "Trauma Bag Capacity: 26L");
    }

    [Fact]
    public async Task ImportPgomAsync_NameOver300_IsTruncatedAndNoted()
    {
        (PriceIndexService sut, _, List<PriceIndexItem> seed) = BuildPgom(
            [Pgom(2, "L-1", new string('a', 500), "Cat", "pc", 10m)]);

        ServiceResult<CsvImportResult> result = await sut.ImportPgomAsync(new MemoryStream());

        Assert.Equal(300, seed.Single().Name.Length);
        Assert.Contains(result.Value!.Notes!, n => n.Contains("1 names") && n.Contains("300"));
    }

    [Fact]
    public async Task ImportPgomAsync_ExistingItem_UpdatesPriceAndKeepsActiveFlags()
    {
        List<PriceIndexItem> seed = [Item(1, "Bond paper", "ream", 200m, "Old", daysEnabled: true)];
        seed[0].IsActive = false;
        (PriceIndexService sut, _, _) = BuildPgom([Pgom(2, "X-3", "bond PAPER", "Office Supplies", "Ream", 250m)], seed);

        ServiceResult<CsvImportResult> result = await sut.ImportPgomAsync(new MemoryStream());

        Assert.Equal(1, result.Value!.Updated);
        Assert.Equal(0, result.Value.New);
        Assert.Equal(250m, seed.Single().UnitPrice);
        Assert.Equal("Office Supplies", seed.Single().Category);
        Assert.False(seed.Single().IsActive);       // the export has no flags — leave them alone
        Assert.True(seed.Single().DaysEnabled);
    }

    [Fact]
    public async Task ImportPgomAsync_ReimportOfSameFile_ChangesNothing()
    {
        List<PriceIndexImportRow> rows = [Pgom(2, "X-3", "Bond paper", "Office Supplies", "ream", 250m)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo, _) = BuildPgom(rows);
        await sut.ImportPgomAsync(new MemoryStream());

        ServiceResult<CsvImportResult> second = await sut.ImportPgomAsync(new MemoryStream());

        Assert.Equal(0, second.Value!.New);
        Assert.Equal(0, second.Value.Updated);
        Assert.Equal(1, second.Value.Skipped);
        repo.Verify(r => r.UpdateAsync(It.IsAny<PriceIndexItem>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportPgomAsync_BadRows_AreSkippedWithRowNumber_GoodRowsStillImport()
    {
        (PriceIndexService sut, _, List<PriceIndexItem> seed) = BuildPgom(
        [
            Pgom(2, "A-1", "Good", "Cat", "pc", 5m),
            Pgom(3, "A-2", "Bad price", "Cat", "pc", null, error: "Price 'abc' is not a valid number."),
            Pgom(4, "A-3", "Negative", "Cat", "pc", -1m),
            Pgom(5, "A-4", "", "Cat", "pc", 5m),
            Pgom(6, new string('c', 51), "Long code", "Cat", "pc", 5m),
        ]);

        ServiceResult<CsvImportResult> result = await sut.ImportPgomAsync(new MemoryStream());

        Assert.Equal(1, result.Value!.New);
        Assert.Equal(4, result.Value.Skipped);
        Assert.Single(seed);
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 3"));
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 4") && e.Contains("negative"));
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 5"));
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 6") && e.Contains("item code"));
    }

    [Fact]
    public async Task ImportPgomAsync_MissingRequiredColumn_ReturnsBadRequestAndWritesNothing()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParsePriceIndexImport(It.IsAny<Stream>()))
            .Throws(new ImportParseException(["Missing required column 'Price'."]));
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build([], excel: excel.Object);

        ServiceResult<CsvImportResult> result = await sut.ImportPgomAsync(new MemoryStream());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("Price", result.Error!);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportPgomAsync_UnreadableFile_ReturnsBadRequest()
    {
        Mock<IExcelService> excel = new();
        excel.Setup(e => e.ParsePriceIndexImport(It.IsAny<Stream>())).Throws(new InvalidDataException("not a zip"));
        (PriceIndexService sut, _) = Build([], excel: excel.Object);

        ServiceResult<CsvImportResult> result = await sut.ImportPgomAsync(new MemoryStream());

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains(".xlsx", result.Error!);
    }

    [Fact]
    public async Task ImportCsvAsync_EmptyFile_ReturnsBadRequest()
    {
        (PriceIndexService sut, _) = Build([]);
        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync("");
        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
    }

    [Fact]
    public async Task ImportCsvAsync_XlsxUploadedAsCsv_ReturnsBadRequestAndWritesNothing()
    {
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build([]);

        // An .xlsx is a zip: it begins "PK\x03\x04" and carries NUL bytes once decoded as text.
        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync("PK\u0003\u0004\0\0\0 xl/worksheets/sheet1.xml");

        Assert.Equal(ServiceErrorCode.BadRequest, result.Code);
        Assert.Contains("CSV", result.Error!);
        repo.Verify(r => r.AddAsync(It.IsAny<PriceIndexItem>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ImportCsvAsync_OverlongFields_SkipsRowWithColumnNamedError_KeepsGoodRows()
    {
        (PriceIndexService sut, _) = Build([]);

        string longStock = new('x', 51);
        string csv =
            "name,unit,unit_price,category,is_active,days_enabled,stock_card_no\n" +
            "Good item,piece,10,Cat,true,false,OS-1\n" +
            $"Bad stock,piece,10,Cat,true,false,{longStock}\n" +
            $"{new string('n', 301)},piece,10,Cat,true,false,\n" +
            $"Bad unit,{new string('u', 51)},10,Cat,true,false,\n" +
            $"Bad cat,piece,10,{new string('c', 101)},true,false,\n";

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value!.New);
        Assert.Equal(4, result.Value.Skipped);
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 3") && e.Contains("stock_card_no") && e.Contains("50"));
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 4") && e.Contains("name") && e.Contains("300"));
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 5") && e.Contains("unit") && e.Contains("50"));
        Assert.Contains(result.Value.Errors, e => e.StartsWith("Row 6") && e.Contains("category") && e.Contains("100"));
    }

    [Fact]
    public async Task ImportCsvAsync_OverlongStockCardNoOnExistingRow_SkipsWithoutMutating()
    {
        List<PriceIndexItem> seed = [Item(1, "Bond paper", "ream", 494m, "Paper", stockCardNo: "OS-1")];
        (PriceIndexService sut, _) = Build(seed);

        string csv =
            "name,unit,unit_price,category,is_active,days_enabled,stock_card_no\n" +
            $"Bond paper,ream,600,Paper,true,false,{new string('x', 60)}\n";

        ServiceResult<CsvImportResult> result = await sut.ImportCsvAsync(csv);

        Assert.Equal(1, result.Value!.Skipped);
        Assert.Equal(494m, seed.Single().UnitPrice);
        Assert.Equal("OS-1", seed.Single().StockCardNo);
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

    // ── Count + picker reads (RAL-232) ─────────────────────────────────────────
    // The Config dashboard tile downloaded the whole ~1.57 MB catalogue to render .length, and
    // both item pickers pulled all nine fields when they read five. Both now have their own
    // SQL-side read; these tests exist mainly to guard that they never quietly fall back to the
    // full-list path, which would restore the bug while still returning correct-looking values.

    [Fact]
    public async Task GetCountAsync_ReturnsCountFromRepository_WithoutReadingTheList()
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m), Item(2, "Bond Paper", "ream", 250m)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed);

        int count = await sut.GetCountAsync(null, ActiveFilter.All);

        Assert.Equal(2, count);
        repo.Verify(r => r.CountAsync(null, null, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ActiveFilter.Active, true)]
    [InlineData(ActiveFilter.Inactive, false)]
    [InlineData(ActiveFilter.All, null)]
    public async Task GetCountAsync_TranslatesActiveFilterForSql(ActiveFilter filter, bool? expected)
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m), Item(2, "Old Item", "piece", 5m, active: false)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed);

        await sut.GetCountAsync("bond", filter);

        repo.Verify(r => r.CountAsync(expected, "bond", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetCountAsync_ExcludesInactive_WhenFilteredToActive()
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m), Item(2, "Retired", "piece", 5m, active: false)];
        (PriceIndexService sut, _) = Build(seed);

        Assert.Equal(1, await sut.GetCountAsync(null, ActiveFilter.Active));
    }

    [Fact]
    public async Task GetPickerListAsync_MapsTheFiveFieldsPickersActuallyUse()
    {
        List<PriceIndexItem> seed =
        [
            Item(7, "Bond Paper", "ream", 250m, category: "Office Supplies",
                 daysEnabled: true, stockCardNo: "SC-001"),
        ];
        (PriceIndexService sut, _) = Build(seed);

        IReadOnlyList<PriceIndexPickerItemDto> items = await sut.GetPickerListAsync(null, ActiveFilter.Active);

        PriceIndexPickerItemDto only = Assert.Single(items);
        Assert.Equal(new PriceIndexPickerItemDto(7, "Bond Paper", "ream", 250m, true), only);
    }

    [Fact]
    public async Task GetPickerListAsync_UsesTheProjectedRead_NotTheFullList()
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed);

        await sut.GetPickerListAsync(null, ActiveFilter.Active);

        repo.Verify(r => r.GetPickerItemsAsync(true, null, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ActiveFilter.Active, true)]
    [InlineData(ActiveFilter.Inactive, false)]
    [InlineData(ActiveFilter.All, null)]
    public async Task GetPickerListAsync_TranslatesActiveFilterForSql(ActiveFilter filter, bool? expected)
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed);

        await sut.GetPickerListAsync("pen", filter);

        repo.Verify(r => r.GetPickerItemsAsync(expected, "pen", It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>
    /// The management grid must keep every field — the whole point of adding a separate picker
    /// read rather than narrowing the shared DTO.
    /// </summary>
    [Fact]
    public async Task GetAllAsync_StillReturnsTheFullRecord_AfterThePickerSplit()
    {
        List<PriceIndexItem> seed =
        [
            Item(1, "Bond Paper", "ream", 250m, category: "Office Supplies", stockCardNo: "SC-001"),
        ];
        (PriceIndexService sut, _) = Build(seed);

        PriceIndexItemDto only = Assert.Single(await sut.GetAllAsync(null, ActiveFilter.Active));

        Assert.Equal("Office Supplies", only.Category);
        Assert.Equal("SC-001", only.StockCardNo);
        Assert.True(only.IsActive);
        Assert.Equal(FixedNow, only.PriceUpdatedAt);
    }

    // ── Paged read (RAL-233) — the management grid ──────────────────────────────
    // Server-side pagination/sort is the exact scenario DataTable's own doc comment warns
    // about: client re-sort would silently sort only the current page. These tests exist to
    // guard that GetPagedAsync's page/sort/count all come from the SQL-side read, never a
    // full materialize-then-slice.

    [Fact]
    public async Task GetPagedAsync_ReturnsRequestedPageSize_NotTheWholeCatalogue()
    {
        List<PriceIndexItem> seed = Enumerable.Range(1, 5)
            .Select(i => Item(i, $"Item {i:00}", "piece", i * 10m)).ToList();
        (PriceIndexService sut, _) = Build(seed);

        PriceIndexPageDto result = await sut.GetPagedAsync(
            null, ActiveFilter.All, sortColumn: null, sortDescending: false, page: 1, pageSize: 2);

        Assert.Equal(2, result.Items.Count);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(2, result.PageSize);
    }

    [Fact]
    public async Task GetPagedAsync_SecondPage_ReturnsTheRemainingRows()
    {
        List<PriceIndexItem> seed = Enumerable.Range(1, 5)
            .Select(i => Item(i, $"Item {i:00}", "piece", i * 10m)).ToList();
        (PriceIndexService sut, _) = Build(seed);

        PriceIndexPageDto result = await sut.GetPagedAsync(
            null, ActiveFilter.All, sortColumn: null, sortDescending: false, page: 3, pageSize: 2);

        PriceIndexItemDto only = Assert.Single(result.Items);
        Assert.Equal("Item 05", only.Name);
        Assert.Equal(5, result.TotalCount);
    }

    [Fact]
    public async Task GetPagedAsync_TotalCountReflectsTheFilter_NotTheWholeTable()
    {
        List<PriceIndexItem> seed =
        [
            Item(1, "Ballpen", "piece", 15m),
            Item(2, "Retired Item", "piece", 5m, active: false),
        ];
        (PriceIndexService sut, _) = Build(seed);

        PriceIndexPageDto result = await sut.GetPagedAsync(
            null, ActiveFilter.Active, sortColumn: null, sortDescending: false, page: 1, pageSize: 50);

        Assert.Equal(1, result.TotalCount);
        Assert.Equal("Ballpen", Assert.Single(result.Items).Name);
    }

    [Theory]
    [InlineData("unitPrice", false, new[] { "C", "A", "B" })]     // C=5, A=10, B=20
    [InlineData("unitPrice", true,  new[] { "B", "A", "C" })]
    [InlineData("name",      false, new[] { "A", "B", "C" })]     // Apple, Bond Paper, Chair
    [InlineData("daysEnabled", false, new[] { "A", "C", "B" })]   // false, false, true — B is daysEnabled
    public async Task GetPagedAsync_SortsByEachWhitelistedColumn(
        string sortBy, bool descending, string[] expectedOrder)
    {
        List<PriceIndexItem> seed =
        [
            Item(1, "Apple Juice", "box", 10m, daysEnabled: false),   // "A"
            Item(2, "Bond Paper", "ream", 20m, daysEnabled: true),    // "B"
            Item(3, "Chair", "piece", 5m, daysEnabled: false),        // "C"
        ];
        Dictionary<int, string> label = new() { [1] = "A", [2] = "B", [3] = "C" };
        (PriceIndexService sut, _) = Build(seed);

        PriceIndexPageDto result = await sut.GetPagedAsync(
            null, ActiveFilter.All, sortBy, descending, page: 1, pageSize: 50);

        Assert.Equal(expectedOrder, result.Items.Select(i => label[i.Id]).ToArray());
    }

    [Fact]
    public async Task GetPagedAsync_UnrecognizedSortColumn_FallsBackToNameAscending_NotAnError()
    {
        List<PriceIndexItem> seed =
        [
            Item(1, "Zebra Print", "piece", 1m),
            Item(2, "Apple Juice", "box", 1m),
        ];
        (PriceIndexService sut, _) = Build(seed);

        // "'; DROP TABLE" is deliberate — this is the injection-surface case ApplySort's own
        // comment calls out, not just a typo. It must fall back safely, never reach OrderBy raw.
        PriceIndexPageDto result = await sut.GetPagedAsync(
            null, ActiveFilter.All, "'; DROP TABLE price_index_items;--", sortDescending: false,
            page: 1, pageSize: 50);

        Assert.Equal(new[] { "Apple Juice", "Zebra Print" }, result.Items.Select(i => i.Name).ToArray());
    }

    [Fact]
    public async Task GetPagedAsync_UsesTheProjectedPagedRead_NotTheFullList()
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed);

        await sut.GetPagedAsync(null, ActiveFilter.All, null, false, 1, 50);

        repo.Verify(r => r.GetPagedAsync(
            null, null, null, false, 1, 50, It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetFilteredAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(ActiveFilter.Active, true)]
    [InlineData(ActiveFilter.Inactive, false)]
    [InlineData(ActiveFilter.All, null)]
    public async Task GetPagedAsync_TranslatesActiveFilterForSql(ActiveFilter filter, bool? expected)
    {
        List<PriceIndexItem> seed = [Item(1, "Ballpen", "piece", 15m)];
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build(seed);

        await sut.GetPagedAsync("pen", filter, "unit", true, 2, 25);

        repo.Verify(r => r.GetPagedAsync(
            expected, "pen", "unit", true, 2, 25, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetPickerETagAsync (PPDO-183) ─────────────────────────────────────────

    private static async Task<string> ETagFor(int count, DateTime? lastUpdatedAt)
    {
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build([]);
        repo.Setup(r => r.GetVersionStampAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((count, lastUpdatedAt));
        return await sut.GetPickerETagAsync();
    }

    [Fact]
    public async Task GetPickerETagAsync_ReturnsWeakTagFromShapeCountAndLatestUpdate()
    {
        string etag = await ETagFor(6398, FixedNow);

        Assert.Equal($"W/\"pi{PriceIndexService.PickerShapeVersion}-6398-{FixedNow.Ticks}\"", etag);
    }

    [Fact]
    public async Task GetPickerETagAsync_EmptyCatalogue_ReturnsStableTag()
        => Assert.Equal($"W/\"pi{PriceIndexService.PickerShapeVersion}-0-0\"", await ETagFor(0, null));

    [Fact]
    public async Task GetPickerETagAsync_AnEditMovesTheLatestUpdate_ChangesTheTag()
        => Assert.NotEqual(await ETagFor(6398, FixedNow), await ETagFor(6398, FixedNow.AddSeconds(1)));

    [Fact]
    public async Task GetPickerETagAsync_ARowRemoved_ChangesTheTag()
        => Assert.NotEqual(await ETagFor(6398, FixedNow), await ETagFor(6397, FixedNow));

    [Fact]
    public async Task GetPickerETagAsync_NeverLoadsTheCatalogue()
    {
        (PriceIndexService sut, Mock<IPriceIndexItemRepository> repo) = Build([]);
        repo.Setup(r => r.GetVersionStampAsync(It.IsAny<CancellationToken>())).ReturnsAsync((1, FixedNow));

        await sut.GetPickerETagAsync();

        repo.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
        repo.Verify(r => r.GetPickerItemsAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
