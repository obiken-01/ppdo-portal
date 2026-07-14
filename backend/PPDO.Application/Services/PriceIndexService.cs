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
    private static readonly string[] CsvHeaders = { "name", "unit", "unit_price", "category", "is_active", "days_enabled" };

    private readonly IRepository<PriceIndexItem> _repo;
    private readonly ILogger<PriceIndexService> _logger;
    private readonly IAuditService _audit;

    public PriceIndexService(IRepository<PriceIndexItem> repo, ILogger<PriceIndexService> logger, IAuditService audit)
    {
        _repo   = repo;
        _logger = logger;
        _audit  = audit;
    }

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<PriceIndexItemDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IEnumerable<PriceIndexItem> q = await _repo.GetAllAsync(cancellationToken);

        q = active switch
        {
            ActiveFilter.Active   => q.Where(p => p.IsActive),
            ActiveFilter.Inactive => q.Where(p => !p.IsActive),
            _                     => q,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            q = q.Where(p =>
                p.Name.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                (p.Category != null && p.Category.Contains(s, StringComparison.OrdinalIgnoreCase)));
        }

        return q.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(MapToDto)
                .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PriceIndexItemDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        PriceIndexItem? p = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
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
        IReadOnlyList<PriceIndexItem> all = await _repo.GetAllAsync(cancellationToken);
        if (all.Any(p => SameKey(p, name, unit)))
            return ServiceResult<PriceIndexItemDto>.Conflict($"A price index item named '{name}' ({unit}) already exists.");

        DateTime now = DateTime.UtcNow;
        PriceIndexItem entity = new()
        {
            Name           = name,
            Unit           = unit,
            UnitPrice      = dto.UnitPrice,
            Category       = Blank(dto.Category),
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
            newValues: new { entity.Name, entity.Unit, entity.UnitPrice, entity.IsActive, entity.DaysEnabled },
            cancellationToken);
        return ServiceResult<PriceIndexItemDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PriceIndexItemDto>> UpdateAsync(int id, UpsertPriceIndexItemDto dto, CancellationToken cancellationToken = default)
    {
        string? validationError = ValidateFields(dto.Name, dto.Unit, dto.UnitPrice);
        if (validationError is not null)
            return ServiceResult<PriceIndexItemDto>.BadRequest(validationError);

        IReadOnlyList<PriceIndexItem> all = await _repo.GetAllAsync(cancellationToken);
        PriceIndexItem? entity = all.FirstOrDefault(p => p.Id == id);
        if (entity is null)
            return ServiceResult<PriceIndexItemDto>.NotFound($"Price index item {id} not found.");

        string name = dto.Name.Trim();
        string unit = dto.Unit.Trim();
        if (all.Any(p => p.Id != id && SameKey(p, name, unit)))
            return ServiceResult<PriceIndexItemDto>.Conflict($"A price index item named '{name}' ({unit}) already exists.");

        var oldSnapshot = new { entity.Name, entity.Unit, entity.UnitPrice, entity.IsActive, entity.DaysEnabled };
        DateTime now = DateTime.UtcNow;

        entity.Name        = name;
        entity.Unit        = unit;
        entity.Category    = Blank(dto.Category);
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
            newValues: new { entity.Name, entity.Unit, entity.UnitPrice, entity.IsActive, entity.DaysEnabled },
            cancellationToken);
        return ServiceResult<PriceIndexItemDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PriceIndexItemDto>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        PriceIndexItem? entity = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(p => p.Id == id);
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
                p.IsActive ? "true" : "false", p.DaysEnabled ? "true" : "false",
            });
        return Csv.Write(CsvHeaders, rows);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default)
    {
        List<string[]> parsed = Csv.Parse(csvText);
        if (parsed.Count == 0)
            return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

        int start = LooksLikeHeader(parsed[0], "unit_price") ? 1 : 0;

        List<PriceIndexItem> all = (await _repo.GetAllAsync(cancellationToken)).ToList();
        Dictionary<string, PriceIndexItem> byKey = all.ToDictionary(
            p => Key(p.Name, p.Unit), p => p, StringComparer.OrdinalIgnoreCase);

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

            if (name.Length == 0 || unit.Length == 0)
            {
                skipped++;
                errors.Add($"Row {rowNumber}: name and unit are required.");
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

            string key = Key(name, unit);
            if (byKey.TryGetValue(key, out PriceIndexItem? existing))
            {
                bool priceChanged = existing.UnitPrice != price;
                bool changed =
                    priceChanged ||
                    Blank(existing.Category) != Blank(category) ||
                    existing.IsActive != active ||
                    existing.DaysEnabled != daysEnabled;

                if (!changed) { skipped++; continue; }

                existing.UnitPrice = price;
                if (priceChanged) existing.PriceUpdatedAt = now;
                existing.Category    = Blank(category);
                existing.IsActive    = active;
                existing.DaysEnabled = daysEnabled;
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
                    PriceUpdatedAt = now,
                    IsActive       = active,
                    DaysEnabled    = daysEnabled,
                    CreatedAt      = now,
                    UpdatedAt      = now,
                };
                await _repo.AddAsync(entity, cancellationToken);
                byKey[key] = entity;   // guard against duplicate keys within the same file
                created++;
            }
        }

        await _repo.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Price index CSV imported. New: {New}, Updated: {Updated}, Skipped: {Skipped}", created, updated, skipped);
        return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped, errors));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static string? ValidateFields(string name, string unit, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Name is required.";
        if (string.IsNullOrWhiteSpace(unit)) return "Unit is required.";
        if (unitPrice < 0) return "Unit price cannot be negative.";
        return null;
    }

    private static bool SameKey(PriceIndexItem p, string name, string unit) =>
        p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
        p.Unit.Equals(unit, StringComparison.OrdinalIgnoreCase);

    private static string Key(string name, string unit) => $"{name}|{unit}";

    private static PriceIndexItemDto MapToDto(PriceIndexItem p) =>
        new(p.Id, p.Name, p.Unit, p.UnitPrice, p.Category, p.PriceUpdatedAt, p.IsActive, p.DaysEnabled);

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
