using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Enums;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// CCET climate-change typology config CRUD (RAL-247). Soft delete only; Code is the unique key.
/// The table is small (~60 rows, one per code the province uses), so filtering happens in memory —
/// the same call the funding_sources config makes for the same reason.
/// </summary>
public sealed class ClimateChangeTypologyService : IClimateChangeTypologyService
{
    /// <summary>The two CCET categories, plus the bucket for a code that follows neither.</summary>
    private static readonly string[] Categories = ["Adaptation", "Mitigation", "Unclassified"];

    private readonly IClimateChangeTypologyRepository      _repo;
    private readonly ILogger<ClimateChangeTypologyService> _logger;
    private readonly IAuditService                         _audit;

    public ClimateChangeTypologyService(
        IClimateChangeTypologyRepository repo,
        ILogger<ClimateChangeTypologyService> logger,
        IAuditService audit)
    {
        _repo   = repo;
        _logger = logger;
        _audit  = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClimateChangeTypologyDto>> GetAllAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IEnumerable<ClimateChangeTypology> q = await _repo.GetAllAsync(cancellationToken);

        q = active switch
        {
            ActiveFilter.Active   => q.Where(t => t.IsActive),
            ActiveFilter.Inactive => q.Where(t => !t.IsActive),
            _                     => q,
        };

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            q = q.Where(t =>
                t.Code.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                t.Name.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
                .Select(MapToDto)
                .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ClimateChangeTypologyDto>> GetByIdAsync(
        int id, CancellationToken cancellationToken = default)
    {
        ClimateChangeTypology? t =
            (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
        return t is null
            ? ServiceResult<ClimateChangeTypologyDto>.NotFound($"Climate change typology {id} not found.")
            : ServiceResult<ClimateChangeTypologyDto>.Ok(MapToDto(t));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ClimateChangeTypologyDto>> CreateAsync(
        UpsertClimateChangeTypologyDto dto, CancellationToken cancellationToken = default)
    {
        string? invalid = Validate(dto);
        if (invalid is not null) return ServiceResult<ClimateChangeTypologyDto>.BadRequest(invalid);

        string code = Normalize(dto.Code);
        IReadOnlyList<ClimateChangeTypology> all = await _repo.GetAllAsync(cancellationToken);
        if (all.Any(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<ClimateChangeTypologyDto>.Conflict(
                $"Climate change typology code '{code}' already exists.");

        DateTime now = DateTime.UtcNow;
        ClimateChangeTypology entity = new()
        {
            Code        = code,
            Name        = dto.Name.Trim(),
            Category    = dto.Category.Trim(),
            Description = Blank(dto.Description),
            IsActive    = dto.IsActive,
            CreatedAt   = now,
            UpdatedAt   = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Climate change typology created. Code: {Code}", entity.Code);
        await _audit.LogAsync("climate_change_typologies", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.Code, entity.Name, entity.Category, entity.IsActive },
            cancellationToken);
        return ServiceResult<ClimateChangeTypologyDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ClimateChangeTypologyDto>> UpdateAsync(
        int id, UpsertClimateChangeTypologyDto dto, CancellationToken cancellationToken = default)
    {
        string? invalid = Validate(dto);
        if (invalid is not null) return ServiceResult<ClimateChangeTypologyDto>.BadRequest(invalid);

        IReadOnlyList<ClimateChangeTypology> all = await _repo.GetAllAsync(cancellationToken);
        ClimateChangeTypology? entity = all.FirstOrDefault(t => t.Id == id);
        if (entity is null)
            return ServiceResult<ClimateChangeTypologyDto>.NotFound($"Climate change typology {id} not found.");

        string code = Normalize(dto.Code);
        if (all.Any(t => t.Id != id && t.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<ClimateChangeTypologyDto>.Conflict(
                $"Climate change typology code '{code}' already exists.");

        var oldSnapshot = new { entity.Code, entity.Name, entity.Category, entity.IsActive };

        entity.Code        = code;
        entity.Name        = dto.Name.Trim();
        entity.Category    = dto.Category.Trim();
        entity.Description = Blank(dto.Description);
        entity.IsActive    = dto.IsActive;
        entity.UpdatedAt   = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("climate_change_typologies", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.Code, entity.Name, entity.Category, entity.IsActive },
            cancellationToken);
        return ServiceResult<ClimateChangeTypologyDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<int> GetCountAsync(
        string? search, ActiveFilter active, CancellationToken cancellationToken = default)
        // Pushed to SQL rather than counting GetAllAsync in memory — the tile that consumes this
        // is the one the next config page copies, and RAL-232 is what that habit cost at scale.
        => await _repo.CountAsync(ToIsActive(active), search, cancellationToken);

    /// <inheritdoc />
    public async Task<ServiceResult<ClimateChangeTypologyDto>> DeleteAsync(
        int id, CancellationToken cancellationToken = default)
    {
        ClimateChangeTypology? entity =
            (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(t => t.Id == id);
        if (entity is null)
            return ServiceResult<ClimateChangeTypologyDto>.NotFound($"Climate change typology {id} not found.");

        // Soft delete only: AIP activities reference these codes, and an AIP is an audited
        // document — a code that vanishes makes a historical activity unreadable.
        // Capture the prior state: deactivating an already-inactive row must not log a
        // true -> false transition that never happened (RAL-246 -- the audit log is read
        // back in Recent Activity, so a false entry is worse than no entry).
        bool wasActive  = entity.IsActive;

        entity.IsActive  = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Climate change typology deactivated. Code: {Code}", entity.Code);
        await _audit.LogAsync("climate_change_typologies", entity.Id, AuditAction.Delete,
            oldValues: new { entity.Code, entity.Name, entity.Category, IsActive = wasActive },
            newValues: new { entity.Code, entity.Name, entity.Category, entity.IsActive },
            cancellationToken);
        return ServiceResult<ClimateChangeTypologyDto>.Ok(MapToDto(entity));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static string? Validate(UpsertClimateChangeTypologyDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Code))     return "Code is required.";
        if (string.IsNullOrWhiteSpace(dto.Name))     return "Name is required.";
        if (string.IsNullOrWhiteSpace(dto.Category)) return "Category is required.";

        if (!Categories.Contains(dto.Category.Trim(), StringComparer.OrdinalIgnoreCase))
            return $"Category must be one of: {string.Join(", ", Categories)}.";

        // A code carrying a separator is a pasted multi-code value, not a code. Letting one in
        // recreates the free-text field this table exists to replace — the FY2027 data already
        // holds 18 such values, in both comma and semicolon form.
        if (dto.Code.Contains(',') || dto.Code.Contains(';'))
            return "Code must be a single code — enter each part of a multi-code value separately.";

        return null;
    }

    /// <summary>Maps the tri-state filter to the repository's nullable flag; null = no filter.</summary>
    private static bool? ToIsActive(ActiveFilter active) => active switch
    {
        ActiveFilter.Active   => true,
        ActiveFilter.Inactive => false,
        _                     => null,
    };

    private static string Normalize(string code) => code.Trim().ToUpperInvariant();

    private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private static ClimateChangeTypologyDto MapToDto(ClimateChangeTypology t) =>
        new(t.Id, t.Code, t.Name, t.Category, t.Description, t.IsActive);
}
