using System.Globalization;
using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Price Index config CRUD + CSV upsert/export (v1.4 — RAL-118): a procurement item
/// name + unit price catalogue searched from the WFP procurement line-item entry
/// screen (RAL-125).
///
/// Data originates from GSO's own application — currently downloaded as an Excel
/// file and uploaded here via CSV (docs/v1.4/WFP_Rework_Requirements_Draft.md
/// §7.1) — so <see cref="ImportCsvAsync"/> is the PRIMARY real-world ingestion
/// path, not a bonus feature, and gives specific per-row errors on malformed input
/// rather than failing the whole file.
///
/// Soft delete only (IsActive = false). (Name, Unit) is the unique key — there is
/// no natural external code for a GSO price item.
/// </summary>
public sealed class PriceIndexService : IPriceIndexService
{
    // stock_card_no is appended LAST on purpose (v1.5): the importer reads columns positionally,
    // so a CSV exported before this column existed still imports cleanly — Field() returns "" for
    // the missing index rather than shifting every other column.
    private static readonly string[] CsvHeaders = { "name", "unit", "unit_price", "category", "is_active", "days_enabled", "stock_card_no" };

    private readonly IPriceIndexItemRepository _repo;
    private readonly ILogger<PriceIndexService> _logger;
    private readonly IAuditService _audit;
    private readonly IExcelService _excel;

    public PriceIndexService(
        IPriceIndexItemRepository repo, ILogger<PriceIndexService> logger, IAuditService audit, IExcelService excel)
    {
        _repo   = repo;
        _logger = logger;
        _audit  = audit;
        _excel  = excel;
    }

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexItemDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PriceIndexItem> items =
            await _repo.GetFilteredAsync(ToIsActive(active), search, cancellationToken);
        return items.Select(MapToDto).ToList();
    }

    /// <inheritdoc />
    public Task<int> GetCountAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
        => _repo.CountAsync(ToIsActive(active), search, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexPickerItemDto>> GetPickerListAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PriceIndexPickerItem> items =
            await _repo.GetPickerItemsAsync(ToIsActive(active), search, cancellationToken);

        return items
            .Select(i => new PriceIndexPickerItemDto(i.Id, i.Name, i.Unit, i.UnitPrice, i.DaysEnabled, i.StockCardNo))
            .ToList();
    }

    /// <summary>
    /// Bump when <see cref="PriceIndexPickerItemDto"/>'s shape changes, so browsers holding the old
    /// shape refetch even though no row changed.
    /// </summary>
    public const string PickerShapeVersion = "2";

    /// <inheritdoc />
    public async Task<string> GetPickerETagAsync(CancellationToken cancellationToken = default)
    {
        (int count, DateTime? lastUpdatedAt) = await _repo.GetVersionStampAsync(cancellationToken);
        // Weak (W/): the response compression middleware sends the same content gzip, Brotli or
        // plain, so the bytes differ by encoding while the meaning does not.
        return $"W/\"pi{PickerShapeVersion}-{count}-{lastUpdatedAt?.Ticks ?? 0}\"";
    }

    /// <inheritdoc />
    public async Task<PriceIndexPageDto> GetPagedAsync(
        string? search, ActiveFilter active, string? sortColumn, bool sortDescending,
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        (IReadOnlyList<PriceIndexItem> items, int totalCount) = await _repo.GetPagedAsync(
            ToIsActive(active), search, sortColumn, sortDescending, page, pageSize, cancellationToken);

        return new PriceIndexPageDto(items.Select(MapToDto).ToList(), totalCount, page, pageSize);
    }

    /// <summary>Shared by the list, count and picker reads so their filters can't drift apart.</summary>
    private static bool? ToIsActive(ActiveFilter active) => active switch
    {
        ActiveFilter.Active   => true,
        ActiveFilter.Inactive => false,
        _                     => null,
    };

    /// <inheritdoc />
    public async Task<ServiceResult<PriceIndexItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        PriceIndexItem? p = await _repo.GetByIntIdAsync(id, cancellationToken);
        return p is null
            ? ServiceResult<PriceIndexItemDto>.NotFound($"Price index item {id} not found.")
            : ServiceResult<PriceIndexItemDto>.Ok(MapToDto(p));
    }

    // ── Mutations ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<PriceIndexItemDto>> CreateAsync(UpsertPriceIndexItemDto dto, CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateFields(dto.Name, dto.Unit, dto.UnitPrice);
        if (validationError is not null)
            return ServiceResult<PriceIndexItemDto>.BadRequest(validationError);

        string name = dto.Name.Trim();
        string unit = dto.Unit.Trim();
        string? stockCardNo = Blank(dto.StockCardNo);
        // PPDO-186: one EXISTS on the unique index, not the whole ~6,400-row catalogue.
        if (await _repo.ItemExistsAsync(name, unit, stockCardNo, excludeId: null, cancellationToken))
            return ServiceResult<PriceIndexItemDto>.Conflict(DuplicateMessage(name, unit, stockCardNo));

        DateTime now = DateTime.UtcNow;
        PriceIndexItem entity = new()
        {
            Name           = name,
            Unit           = unit,
            UnitPrice      = dto.UnitPrice,
            Category       = Blank(dto.Category),
            StockCardNo    = stockCardNo,
            PriceUpdatedAt = now,
            IsActive       = dto.IsActive,
            DaysEnabled    = dto.DaysEnabled,
            CreatedAt      = now,
            UpdatedAt      = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Price index item created. Name: {Name}, Unit: {Unit}", entity.Name, entity.Unit);
        await _audit.LogAsync("price_index_items", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.Name, entity.Unit, entity.UnitPrice, entity.IsActive, entity.DaysEnabled, entity.StockCardNo },
            cancellationToken);
        return ServiceResult<PriceIndexItemDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PriceIndexItemDto>> UpdateAsync(int id, UpsertPriceIndexItemDto dto, CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateFields(dto.Name, dto.Unit, dto.UnitPrice);
        if (validationError is not null)
            return ServiceResult<PriceIndexItemDto>.BadRequest(validationError);

        PriceIndexItem? entity = await _repo.GetByIntIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<PriceIndexItemDto>.NotFound($"Price index item {id} not found.");

        string name = dto.Name.Trim();
        string unit = dto.Unit.Trim();
        string? stockCardNo = Blank(dto.StockCardNo);
        if (await _repo.ItemExistsAsync(name, unit, stockCardNo, excludeId: id, cancellationToken))
            return ServiceResult<PriceIndexItemDto>.Conflict(DuplicateMessage(name, unit, stockCardNo));

        var oldSnapshot = new { entity.Name, entity.Unit, entity.UnitPrice, entity.IsActive, entity.DaysEnabled, entity.StockCardNo };
        DateTime now = DateTime.UtcNow;

        entity.Name        = name;
        entity.Unit        = unit;
        entity.Category    = Blank(dto.Category);
        entity.StockCardNo = stockCardNo;
        entity.IsActive    = dto.IsActive;
        entity.DaysEnabled = dto.DaysEnabled;
        entity.UpdatedAt   = now;
        if (entity.UnitPrice != dto.UnitPrice)
        {
            entity.UnitPrice      = dto.UnitPrice;
            entity.PriceUpdatedAt = now;
        }

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("price_index_items", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.Name, entity.Unit, entity.UnitPrice, entity.IsActive, entity.DaysEnabled, entity.StockCardNo },
            cancellationToken);
        return ServiceResult<PriceIndexItemDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PriceIndexItemDto>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        PriceIndexItem? entity = await _repo.GetByIntIdAsync(id, cancellationToken);
        if (entity is null)
            return ServiceResult<PriceIndexItemDto>.NotFound($"Price index item {id} not found.");

        entity.IsActive  = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Price index item deactivated. Name: {Name}, Unit: {Unit}", entity.Name, entity.Unit);
        await _audit.LogAsync("price_index_items", entity.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);
        return ServiceResult<PriceIndexItemDto>.Ok(MapToDto(entity));
    }

    // ── CSV ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PriceIndexItem> all = await _repo.GetAllAsync(cancellationToken);
        IEnumerable<string?[]> rows = all
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new string?[]
            {
                p.Name, p.Unit, p.UnitPrice.ToString(CultureInfo.InvariantCulture), p.Category,
                p.IsActive ? "true" : "false", p.DaysEnabled ? "true" : "false", p.StockCardNo,
            });
        return Csv.Write(CsvHeaders, rows);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default)
    {
        if (Csv.LooksBinary(csvText))
            return ServiceResult<CsvImportResult>.BadRequest(Csv.NotCsvMessage);

        List<string[]> parsed = Csv.Parse(csvText);
        if (parsed.Count == 0)
            return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

        int start = LooksLikeHeader(parsed[0], "unit_price") ? 1 : 0;

        List<PriceIndexItem> all = (await _repo.GetAllAsync(cancellationToken)).ToList();
        Dictionary<string, PriceIndexItem> byKey = all.ToDictionary(
            p => Key(p.Name, p.Unit, p.StockCardNo), p => p, StringComparer.OrdinalIgnoreCase);
        // Only used for a 6-column (pre-stock_card_no) file, which cannot say WHICH of several
        // same-name items a row means.
        Dictionary<string, List<PriceIndexItem>> byNameUnit = all
            .GroupBy(p => NameUnitKey(p.Name, p.Unit), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        int created = 0, updated = 0, skipped = 0;
        List<string> errors = new();
        DateTime now = DateTime.UtcNow;

        for (int i = start; i < parsed.Count; i++)
        {
            string[] f = parsed[i];
            int rowNumber = i + 1;
            string name = Field(f, 0).Trim();
            string unit = Field(f, 1).Trim();
            string priceCell = Field(f, 2);
            string category = Field(f, 3);
            bool active = Csv.ParseBool(Field(f, 4), fallback: true);
            bool daysEnabled = Csv.ParseBool(Field(f, 5), fallback: false);
            string stockCardNo = Field(f, 6);
            // A CSV exported before stock_card_no existed has only 6 columns. Treat the absent
            // column as "leave alone" rather than "clear" — otherwise re-importing an older file
            // would silently wipe every stock card number already recorded. A row that HAS the
            // column but leaves it blank still clears it, matching how category behaves.
            bool hasStockCardNoColumn = f.Length > 6;

            if (name.Length == 0 || unit.Length == 0)
            {
                skipped++;
                errors.Add($"Row {rowNumber}: name and unit are required.");
                continue;
            }

            // Mirrors PriceIndexItemConfiguration's HasMaxLength — checked here so one oversized
            // cell skips its row with a named column instead of failing the whole batch at SaveChanges.
            string? tooLong =
                Csv.OverLimit("name", name, NameMax) ??
                Csv.OverLimit("unit", unit, UnitMax) ??
                Csv.OverLimit("category", Blank(category), CategoryMax) ??
                Csv.OverLimit("stock_card_no", Blank(stockCardNo), StockCardNoMax);
            if (tooLong is not null)
            {
                skipped++;
                errors.Add($"Row {rowNumber}: {tooLong}");
                continue;
            }

            if (!decimal.TryParse(priceCell.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out decimal price))
            {
                skipped++;
                errors.Add($"Row {rowNumber}: unit_price '{priceCell}' is not a valid number.");
                continue;
            }

            if (price < 0)
            {
                skipped++;
                errors.Add($"Row {rowNumber}: unit_price cannot be negative.");
                continue;
            }

            string key = Key(name, unit, stockCardNo);
            PriceIndexItem? existing = null;
            if (hasStockCardNoColumn)
            {
                byKey.TryGetValue(key, out existing);
            }
            else if (byNameUnit.TryGetValue(NameUnitKey(name, unit), out List<PriceIndexItem>? sameName))
            {
                if (sameName.Count > 1)
                {
                    skipped++;
                    errors.Add($"Row {rowNumber}: '{name}' ({unit}) exists with {sameName.Count} different stock card numbers and this file has no stock_card_no column to say which one.");
                    continue;
                }
                existing = sameName[0];
            }

            if (existing is not null)
            {
                bool priceChanged = existing.UnitPrice != price;
                bool changed =
                    priceChanged ||
                    Blank(existing.Category) != Blank(category) ||
                    existing.IsActive != active ||
                    existing.DaysEnabled != daysEnabled ||
                    (hasStockCardNoColumn && Blank(existing.StockCardNo) != Blank(stockCardNo));

                if (!changed) { skipped++; continue; }

                existing.UnitPrice = price;
                if (priceChanged) existing.PriceUpdatedAt = now;
                existing.Category    = Blank(category);
                existing.IsActive    = active;
                existing.DaysEnabled = daysEnabled;
                if (hasStockCardNoColumn) existing.StockCardNo = Blank(stockCardNo);
                existing.UpdatedAt   = now;
                await _repo.UpdateAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                PriceIndexItem entity = new()
                {
                    Name           = name,
                    Unit           = unit,
                    UnitPrice      = price,
                    Category       = Blank(category),
                    StockCardNo    = Blank(stockCardNo),
                    PriceUpdatedAt = now,
                    IsActive       = active,
                    DaysEnabled    = daysEnabled,
                    CreatedAt      = now,
                    UpdatedAt      = now,
                };
                await _repo.AddAsync(entity, cancellationToken);
                byKey[key] = entity;   // guard against duplicate keys within the same file
                string nameUnit = NameUnitKey(name, unit);
                if (!byNameUnit.TryGetValue(nameUnit, out List<PriceIndexItem>? bucket))
                    byNameUnit[nameUnit] = bucket = new List<PriceIndexItem>();
                bucket.Add(entity);
                created++;
            }
        }

        await _repo.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Price index CSV imported. New: {New}, Updated: {Updated}, Skipped: {Skipped}", created, updated, skipped);
        return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped, errors));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CsvImportResult>> ImportPgomAsync(
        Stream workbook, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PriceIndexImportRow> rows;
        try
        {
            rows = _excel.ParsePriceIndexImport(workbook);
        }
        catch (ImportParseException ex)
        {
            return ServiceResult<CsvImportResult>.BadRequest(
                "This does not look like a PGOM Items export. " + string.Join(" ", ex.Errors));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read the PGOM price index workbook.");
            return ServiceResult<CsvImportResult>.BadRequest(
                "The uploaded file could not be read. Make sure it is an .xlsx file exported from PGOM.");
        }

        if (rows.Count == 0)
            return ServiceResult<CsvImportResult>.BadRequest("The file has no item rows.");

        List<string> errors = new();
        int skipped = 0, truncated = 0, mergedDuplicates = 0;

        // One entry per (name, unit); the last row in the file wins.
        Dictionary<string, PgomItem> byKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (PriceIndexImportRow row in rows)
        {
            if (row.Error is not null)
            {
                skipped++; errors.Add($"Row {row.RowNumber}: {row.Error}"); continue;
            }

            string name = CleanText(row.Description);
            string unit = CleanText(row.Unit);
            if (name.Length == 0 || unit.Length == 0)
            {
                skipped++; errors.Add($"Row {row.RowNumber}: description and unit are required."); continue;
            }

            if (row.Price is null || row.Price < 0)
            {
                skipped++; errors.Add($"Row {row.RowNumber}: price cannot be negative."); continue;
            }

            if (name.Length > NameMax)
            {
                name = name[..NameMax].TrimEnd();
                truncated++;
            }

            string? category    = Blank(CleanText(row.AccountName));
            string? stockCardNo = Blank(CleanText(row.ItemCode));
            string? tooLong =
                Csv.OverLimit("unit", unit, UnitMax) ??
                Csv.OverLimit("account name", category, CategoryMax) ??
                Csv.OverLimit("item code", stockCardNo, StockCardNoMax);
            if (tooLong is not null)
            {
                skipped++; errors.Add($"Row {row.RowNumber}: {tooLong}"); continue;
            }

            string key = Key(name, unit, stockCardNo);
            if (byKey.ContainsKey(key)) mergedDuplicates++;
            byKey[key] = new PgomItem(name, unit, Math.Round(row.Price.Value, 2, MidpointRounding.AwayFromZero),
                category, stockCardNo);
        }

        List<PriceIndexItem> all = (await _repo.GetAllAsync(cancellationToken)).ToList();
        Dictionary<string, PriceIndexItem> existingByKey = all.ToDictionary(
            p => Key(p.Name, p.Unit, p.StockCardNo), p => p, StringComparer.OrdinalIgnoreCase);
        // Catalogue items with no stock card number yet. The first PGOM row for such an item
        // adopts it (sets its stock card no) instead of leaving a stock-card-less twin behind.
        Dictionary<string, PriceIndexItem> adoptable = all
            .Where(p => Blank(p.StockCardNo) is null)
            .ToDictionary(p => NameUnitKey(p.Name, p.Unit), p => p, StringComparer.OrdinalIgnoreCase);

        int created = 0, updated = 0, unchanged = 0;
        DateTime now = DateTime.UtcNow;

        foreach (PgomItem item in byKey.Values)
        {
            if (!existingByKey.TryGetValue(Key(item.Name, item.Unit, item.StockCardNo), out PriceIndexItem? existing)
                && item.StockCardNo is not null
                && adoptable.Remove(NameUnitKey(item.Name, item.Unit), out PriceIndexItem? adopted))
            {
                existing = adopted;
            }

            if (existing is not null)
            {
                bool priceChanged = existing.UnitPrice != item.Price;
                bool changed =
                    priceChanged ||
                    Blank(existing.Category) != item.Category ||
                    Blank(existing.StockCardNo) != item.StockCardNo;
                if (!changed) { unchanged++; continue; }

                existing.UnitPrice   = item.Price;
                if (priceChanged) existing.PriceUpdatedAt = now;
                existing.Category    = item.Category;
                existing.StockCardNo = item.StockCardNo;
                existing.UpdatedAt   = now;
                await _repo.UpdateAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                await _repo.AddAsync(new PriceIndexItem
                {
                    Name           = item.Name,
                    Unit           = item.Unit,
                    UnitPrice      = item.Price,
                    Category       = item.Category,
                    StockCardNo    = item.StockCardNo,
                    PriceUpdatedAt = now,
                    IsActive       = true,
                    DaysEnabled    = false,
                    CreatedAt      = now,
                    UpdatedAt      = now,
                }, cancellationToken);
                created++;
            }
        }

        await _repo.SaveChangesAsync(cancellationToken);

        // Informational lines - not invalid rows, so they stay out of Errors.
        List<string> notes = new();
        if (mergedDuplicates > 0)
            notes.Add($"{mergedDuplicates} rows repeated an item already in the file (same name, unit and item code) and were merged; the last row was used.");
        if (truncated > 0)
            notes.Add($"{truncated} names were longer than {NameMax} characters and were shortened.");

        _logger.LogInformation(
            "Price index PGOM workbook imported. New: {New}, Updated: {Updated}, Unchanged: {Unchanged}, Skipped: {Skipped}, Merged: {Merged}, Truncated: {Truncated}",
            created, updated, unchanged, skipped, mergedDuplicates, truncated);
        return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped + unchanged, errors, notes));
    }

    private sealed record PgomItem(string Name, string Unit, decimal Price, string? Category, string? StockCardNo);

    /// <summary>
    /// Flattens a spreadsheet cell to one clean line: strips Excel's literal "_x000d_" escapes,
    /// turns tabs/line breaks into spaces and collapses runs of whitespace.
    /// </summary>
    private static string CleanText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        string noEscapes = System.Text.RegularExpressions.Regex.Replace(value, "_x[0-9A-Fa-f]{4}_", " ");
        return System.Text.RegularExpressions.Regex.Replace(noEscapes, @"\s+", " ").Trim();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? ValidateFields(string name, string unit, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Name is required.";
        if (string.IsNullOrWhiteSpace(unit)) return "Unit is required.";
        if (unitPrice < 0) return "Unit price cannot be negative.";
        return null;
    }

    private const int NameMax = 300;
    private const int UnitMax = 50;
    private const int CategoryMax = 100;
    private const int StockCardNoMax = 50;

    /// <summary>The unique key as a dictionary key: name, unit and stock card no (blank = none).</summary>
    private static string Key(string name, string unit, string? stockCardNo)
        => $"{name}|{unit}|{Blank(stockCardNo)}";

    /// <summary>Name + unit only — for files that carry no stock card column to tell items apart.</summary>
    private static string NameUnitKey(string name, string unit) => $"{name}|{unit}";

    private static string DuplicateMessage(string name, string unit, string? stockCardNo)
        => stockCardNo is null
            ? $"A price index item named '{name}' ({unit}) with no stock card no already exists."
            : $"A price index item named '{name}' ({unit}) with stock card no '{stockCardNo}' already exists.";

    private static PriceIndexItemDto MapToDto(PriceIndexItem p) =>
        new(p.Id, p.Name, p.Unit, p.UnitPrice, p.Category, p.PriceUpdatedAt, p.IsActive, p.DaysEnabled, p.StockCardNo);

    /// <summary>Trims and converts blank to null so "" and null compare equal during upsert.</summary>
    private static string? Blank(string? value)
    {
        string t = (value ?? string.Empty).Trim();
        return t.Length == 0 ? null : t;
    }

    private static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

    private static bool LooksLikeHeader(string[] row, string keyColumn) =>
        row.Any(c => c.Trim().Equals(keyColumn, StringComparison.OrdinalIgnoreCase));
}
