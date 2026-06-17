using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Office config CRUD + CSV upsert/export (RAL-70; extends the read-only RAL-81 shape
/// the User Management dropdown depends on).
/// Soft delete only (IsActive = false). OfficeCode is the unique key.
/// The offices table is tiny (~16 rows) — filtering/upsert happens in-memory.
/// </summary>
public sealed class OfficeService : IOfficeService
{
    private static readonly string[] CsvHeaders = { "office_code", "office_name", "is_active", "office_ref_code" };

    private readonly IRepository<Office> _repo;
    private readonly ILogger<OfficeService> _logger;
    private readonly IAuditService _audit;

    public OfficeService(IRepository<Office> repo, ILogger<OfficeService> logger, IAuditService audit)
    {
        _repo   = repo;
        _logger = logger;
        _audit  = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<OfficeDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IEnumerable<Office> q = await _repo.GetAllAsync(cancellationToken);

        q = active switch
        {
            ActiveFilter.Active   => q.Where(o => o.IsActive),
            ActiveFilter.Inactive => q.Where(o => !o.IsActive),
            _                     => q,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            q = q.Where(o =>
                o.OfficeCode.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                o.OfficeName.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderBy(o => o.OfficeName, StringComparer.OrdinalIgnoreCase)
                .Select(MapToDto)
                .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OfficeDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        Office? o = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
        return o is null
            ? ServiceResult<OfficeDto>.NotFound($"Office {id} not found.")
            : ServiceResult<OfficeDto>.Ok(MapToDto(o));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OfficeDto>> CreateAsync(UpsertOfficeDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.OfficeCode))
            return ServiceResult<OfficeDto>.BadRequest("Office code is required.");
        if (string.IsNullOrWhiteSpace(dto.OfficeName))
            return ServiceResult<OfficeDto>.BadRequest("Office name is required.");

        string code = dto.OfficeCode.Trim();
        IReadOnlyList<Office> all = await _repo.GetAllAsync(cancellationToken);
        if (all.Any(o => o.OfficeCode.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<OfficeDto>.Conflict($"Office code '{code}' already exists.");

        DateTime now = DateTime.UtcNow;
        Office entity = new()
        {
            OfficeCode    = code,
            OfficeName    = dto.OfficeName.Trim(),
            OfficeRefCode = NullIfBlank(dto.OfficeRefCode),
            IsActive      = dto.IsActive,
            CreatedAt     = now,
            UpdatedAt     = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Office created. OfficeCode: {OfficeCode}", entity.OfficeCode);
        await _audit.LogAsync("offices", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.OfficeCode, entity.OfficeName, entity.OfficeRefCode, entity.IsActive },
            cancellationToken);
        return ServiceResult<OfficeDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OfficeDto>> UpdateAsync(int id, UpsertOfficeDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.OfficeCode))
            return ServiceResult<OfficeDto>.BadRequest("Office code is required.");
        if (string.IsNullOrWhiteSpace(dto.OfficeName))
            return ServiceResult<OfficeDto>.BadRequest("Office name is required.");

        IReadOnlyList<Office> all = await _repo.GetAllAsync(cancellationToken);
        Office? entity = all.FirstOrDefault(o => o.Id == id);
        if (entity is null)
            return ServiceResult<OfficeDto>.NotFound($"Office {id} not found.");

        string code = dto.OfficeCode.Trim();
        if (all.Any(o => o.Id != id && o.OfficeCode.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<OfficeDto>.Conflict($"Office code '{code}' already exists.");

        var oldSnapshot = new { entity.OfficeCode, entity.OfficeName, entity.OfficeRefCode, entity.IsActive };

        entity.OfficeCode    = code;
        entity.OfficeName    = dto.OfficeName.Trim();
        entity.OfficeRefCode = NullIfBlank(dto.OfficeRefCode);
        entity.IsActive      = dto.IsActive;
        entity.UpdatedAt     = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("offices", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.OfficeCode, entity.OfficeName, entity.OfficeRefCode, entity.IsActive },
            cancellationToken);
        return ServiceResult<OfficeDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<OfficeDto>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        Office? entity = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(o => o.Id == id);
        if (entity is null)
            return ServiceResult<OfficeDto>.NotFound($"Office {id} not found.");

        entity.IsActive  = false;   // soft delete only — keep for history / FK integrity
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Office deactivated. OfficeCode: {OfficeCode}", entity.OfficeCode);
        await _audit.LogAsync("offices", entity.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);
        return ServiceResult<OfficeDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Office> all = await _repo.GetAllAsync(cancellationToken);
        IEnumerable<string?[]> rows = all
            .OrderBy(o => o.OfficeName, StringComparer.OrdinalIgnoreCase)
            .Select(o => new string?[] { o.OfficeCode, o.OfficeName, o.IsActive ? "true" : "false", o.OfficeRefCode ?? "" });
        return Csv.Write(CsvHeaders, rows);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default)
    {
        List<string[]> parsed = Csv.Parse(csvText);
        if (parsed.Count == 0)
            return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

        int start = parsed[0].Any(c => c.Trim().Equals("office_code", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

        List<Office> all = (await _repo.GetAllAsync(cancellationToken)).ToList();
        Dictionary<string, Office> byCode = all.ToDictionary(
            o => o.OfficeCode.Trim(), o => o, StringComparer.OrdinalIgnoreCase);

        int created = 0, updated = 0, skipped = 0;
        List<string> errors = new();
        DateTime now = DateTime.UtcNow;

        for (int i = start; i < parsed.Count; i++)
        {
            string[] f = parsed[i];
            string  code    = Field(f, 0).Trim();
            string  name    = Field(f, 1);
            bool    active  = Csv.ParseBool(Field(f, 2), fallback: true);
            string? refCode = NullIfBlank(Field(f, 3));

            if (code.Length == 0 || name.Trim().Length == 0)
            {
                skipped++;
                errors.Add($"Row {i + 1}: office_code and office_name are required.");
                continue;
            }

            if (byCode.TryGetValue(code, out Office? existing))
            {
                bool changed = existing.OfficeName    != name.Trim()
                            || existing.IsActive      != active
                            || existing.OfficeRefCode != refCode;
                if (!changed) { skipped++; continue; }

                existing.OfficeName    = name.Trim();
                existing.OfficeRefCode = refCode;
                existing.IsActive      = active;
                existing.UpdatedAt     = now;
                await _repo.UpdateAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                Office entity = new()
                {
                    OfficeCode    = code,
                    OfficeName    = name.Trim(),
                    OfficeRefCode = refCode,
                    IsActive      = active,
                    CreatedAt     = now,
                    UpdatedAt     = now,
                };
                await _repo.AddAsync(entity, cancellationToken);
                byCode[code] = entity;
                created++;
            }
        }

        await _repo.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Offices CSV imported. New: {New}, Updated: {Updated}, Skipped: {Skipped}", created, updated, skipped);
        return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped, errors));
    }

    private static OfficeDto MapToDto(Office o) =>
        new(o.Id, o.OfficeCode, o.OfficeName, o.OfficeRefCode, o.IsActive);

    private static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
