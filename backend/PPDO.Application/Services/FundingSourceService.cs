using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Funding source config CRUD + CSV upsert/export (RAL-70).
/// Soft delete only (IsActive = false). Code is the unique key.
/// The funding_sources table is tiny (~6 rows) — filtering/upsert happens in-memory.
/// </summary>
public sealed class FundingSourceService : IFundingSourceService
{
    private static readonly string[] CsvHeaders = { "code", "name", "description", "is_active" };

    private readonly IRepository<FundingSource> _repo;
    private readonly ILogger<FundingSourceService> _logger;
    private readonly IAuditService _audit;

    public FundingSourceService(IRepository<FundingSource> repo, ILogger<FundingSourceService> logger, IAuditService audit)
    {
        _repo   = repo;
        _logger = logger;
        _audit  = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FundingSourceDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IEnumerable<FundingSource> q = await _repo.GetAllAsync(cancellationToken);

        q = active switch
        {
            ActiveFilter.Active   => q.Where(f => f.IsActive),
            ActiveFilter.Inactive => q.Where(f => !f.IsActive),
            _                     => q,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            q = q.Where(f =>
                f.Code.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                f.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
                .Select(MapToDto)
                .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FundingSourceDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        FundingSource? f = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
        return f is null
            ? ServiceResult<FundingSourceDto>.NotFound($"Funding source {id} not found.")
            : ServiceResult<FundingSourceDto>.Ok(MapToDto(f));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FundingSourceDto>> CreateAsync(UpsertFundingSourceDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Code))
            return ServiceResult<FundingSourceDto>.BadRequest("Code is required.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<FundingSourceDto>.BadRequest("Name is required.");

        string code = dto.Code.Trim();
        IReadOnlyList<FundingSource> all = await _repo.GetAllAsync(cancellationToken);
        if (all.Any(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<FundingSourceDto>.Conflict($"Funding source code '{code}' already exists.");

        DateTime now = DateTime.UtcNow;
        FundingSource entity = new()
        {
            Code        = code,
            Name        = dto.Name.Trim(),
            Description = Blank(dto.Description),
            IsActive    = dto.IsActive,
            CreatedAt   = now,
            UpdatedAt   = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Funding source created. Code: {Code}", entity.Code);
        await _audit.LogAsync("funding_sources", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.Code, entity.Name, entity.IsActive },
            cancellationToken);
        return ServiceResult<FundingSourceDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FundingSourceDto>> UpdateAsync(int id, UpsertFundingSourceDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Code))
            return ServiceResult<FundingSourceDto>.BadRequest("Code is required.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<FundingSourceDto>.BadRequest("Name is required.");

        IReadOnlyList<FundingSource> all = await _repo.GetAllAsync(cancellationToken);
        FundingSource? entity = all.FirstOrDefault(f => f.Id == id);
        if (entity is null)
            return ServiceResult<FundingSourceDto>.NotFound($"Funding source {id} not found.");

        string code = dto.Code.Trim();
        if (all.Any(f => f.Id != id && f.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<FundingSourceDto>.Conflict($"Funding source code '{code}' already exists.");

        var oldSnapshot = new { entity.Code, entity.Name, entity.IsActive };

        entity.Code        = code;
        entity.Name        = dto.Name.Trim();
        entity.Description  = Blank(dto.Description);
        entity.IsActive     = dto.IsActive;
        entity.UpdatedAt    = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("funding_sources", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.Code, entity.Name, entity.IsActive },
            cancellationToken);
        return ServiceResult<FundingSourceDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FundingSourceDto>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        FundingSource? entity = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(f => f.Id == id);
        if (entity is null)
            return ServiceResult<FundingSourceDto>.NotFound($"Funding source {id} not found.");

        entity.IsActive  = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Funding source deactivated. Code: {Code}", entity.Code);
        await _audit.LogAsync("funding_sources", entity.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);
        return ServiceResult<FundingSourceDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FundingSource> all = await _repo.GetAllAsync(cancellationToken);
        IEnumerable<string?[]> rows = all
            .OrderBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .Select(f => new string?[] { f.Code, f.Name, f.Description, f.IsActive ? "true" : "false" });
        return Csv.Write(CsvHeaders, rows);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default)
    {
        List<string[]> parsed = Csv.Parse(csvText);
        if (parsed.Count == 0)
            return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

        int start = parsed[0].Any(c => c.Trim().Equals("code", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

        List<FundingSource> all = (await _repo.GetAllAsync(cancellationToken)).ToList();
        Dictionary<string, FundingSource> byCode = all.ToDictionary(
            f => f.Code.Trim(), f => f, StringComparer.OrdinalIgnoreCase);

        int created = 0, updated = 0, skipped = 0;
        List<string> errors = new();
        DateTime now = DateTime.UtcNow;

        for (int i = start; i < parsed.Count; i++)
        {
            string[] f = parsed[i];
            string code   = Field(f, 0).Trim();
            string name   = Field(f, 1);
            string desc   = Field(f, 2);
            bool   active = Csv.ParseBool(Field(f, 3), fallback: true);

            if (code.Length == 0 || name.Trim().Length == 0)
            {
                skipped++;
                errors.Add($"Row {i + 1}: code and name are required.");
                continue;
            }

            if (byCode.TryGetValue(code, out FundingSource? existing))
            {
                bool changed =
                    existing.Name != name.Trim() ||
                    Blank(existing.Description) != Blank(desc) ||
                    existing.IsActive != active;

                if (!changed) { skipped++; continue; }

                existing.Name        = name.Trim();
                existing.Description = Blank(desc);
                existing.IsActive    = active;
                existing.UpdatedAt   = now;
                await _repo.UpdateAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                FundingSource entity = new()
                {
                    Code        = code,
                    Name        = name.Trim(),
                    Description = Blank(desc),
                    IsActive    = active,
                    CreatedAt   = now,
                    UpdatedAt   = now,
                };
                await _repo.AddAsync(entity, cancellationToken);
                byCode[code] = entity;
                created++;
            }
        }

        await _repo.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Funding sources CSV imported. New: {New}, Updated: {Updated}, Skipped: {Skipped}", created, updated, skipped);
        return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped, errors));
    }

    private static FundingSourceDto MapToDto(FundingSource f) => new(f.Id, f.Code, f.Name, f.Description, f.IsActive);

    private static string? Blank(string? value)
    {
        string t = (value ?? string.Empty).Trim();
        return t.Length == 0 ? null : t;
    }

    private static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;
}
