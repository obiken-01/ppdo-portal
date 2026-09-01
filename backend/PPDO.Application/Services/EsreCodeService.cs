using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services
{
    /// <summary>
    /// eSRE code config CRUD (RAL-248). Soft delete only; Code is the unique key, normalised to
    /// upper case so case alone cannot create a duplicate.
    ///
    /// The table holds four rows, so filtering happens in memory rather than at the database —
    /// the same call the funding_sources and climate_change_typologies configs make, and the
    /// exception to docs/PERFORMANCE_GUIDELINES.md that a fixed tiny vocabulary earns.
    /// </summary>
    public class EsreCodeService : IEsreCodeService
    {
        private readonly IRepository<EsreCode> _repo;
        private readonly ILogger<EsreCodeService> _logger;
        private readonly IAuditService _audit;

        public EsreCodeService(
            IRepository<EsreCode> repo,
            ILogger<EsreCodeService> logger,
            IAuditService audit)
        {
            _repo = repo;
            _logger = logger;
            _audit = audit;
        }

        public async Task<ServiceResult<EsreCodeDto>> CreateAsync(UpsertEsreCodeDto dto, CancellationToken cancellationToken = default)
        {
            string? invalid = Validate(dto);
            if (invalid is not null) return ServiceResult<EsreCodeDto>.BadRequest(invalid);

            string code = Normalize(dto.Code);
            IReadOnlyList<EsreCode> all = await _repo.GetAllAsync(cancellationToken);
            if (all.Any(t => t.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                return ServiceResult<EsreCodeDto>.Conflict(
                    $"eSRE code '{code}' already exists.");

            DateTime now = DateTime.UtcNow;
            EsreCode entity = new()
            {
                Code = code,
                Name = dto.Name.Trim(),
                Description = Blank(dto.Description),
                IsActive = dto.IsActive,
                CreatedAt = now,
                UpdatedAt = now,
            };

            await _repo.AddAsync(entity, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("eSRE code created. Code: {Code}", entity.Code);
            await _audit.LogAsync("esre_codes", entity.Id, AuditAction.Create,
                oldValues: null,
                newValues: new { entity.Code, entity.Name, entity.Description, entity.IsActive },
                cancellationToken);
            return ServiceResult<EsreCodeDto>.Ok(MapToDto(entity));
        }

        /// <inheritdoc />
        public async Task<ServiceResult<EsreCodeDto>> DeleteAsync(
            int id, CancellationToken cancellationToken = default)
        {
            EsreCode? entity =
                (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(t => t.Id == id);
            if (entity is null)
                return ServiceResult<EsreCodeDto>.NotFound($"eSRE code {id} not found.");

            // Soft delete only: AIP activities reference these codes, and an AIP is an audited
            // document — a code that vanishes makes a historical activity unreadable.
            // Capture the prior state: deactivating an already-inactive row must not log a
            // true -> false transition that never happened (RAL-246 -- the audit log is read
            // back in Recent Activity, so a false entry is worse than no entry).
            bool wasActive = entity.IsActive;

            entity.IsActive = false;
            entity.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(entity, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("eSRE code deactivated. Code: {Code}", entity.Code);
            await _audit.LogAsync("esre_codes", entity.Id, AuditAction.Delete,
                oldValues: new { entity.Code, entity.Name, entity.Description, IsActive = wasActive },
                newValues: new { entity.Code, entity.Name, entity.Description, entity.IsActive },
                cancellationToken);
            return ServiceResult<EsreCodeDto>.Ok(MapToDto(entity));
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<EsreCodeDto>> GetAllAsync(
            string? search, ActiveFilter active, CancellationToken cancellationToken = default)
        {
            IEnumerable<EsreCode> q = await _repo.GetAllAsync(cancellationToken);

            q = active switch
            {
                ActiveFilter.Active => q.Where(t => t.IsActive),
                ActiveFilter.Inactive => q.Where(t => !t.IsActive),
                _ => q,
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
        public async Task<ServiceResult<EsreCodeDto>> GetByIdAsync(
            int id, CancellationToken cancellationToken = default)
        {
            EsreCode? t =
                (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
            return t is null
                ? ServiceResult<EsreCodeDto>.NotFound($"eSRE code {id} not found.")
                : ServiceResult<EsreCodeDto>.Ok(MapToDto(t));
        }

        /// <inheritdoc />
        public async Task<ServiceResult<EsreCodeDto>> UpdateAsync(
            int id, UpsertEsreCodeDto dto, CancellationToken cancellationToken = default)
        {
            string? invalid = Validate(dto);
            if (invalid is not null) return ServiceResult<EsreCodeDto>.BadRequest(invalid);

            IReadOnlyList<EsreCode> all = await _repo.GetAllAsync(cancellationToken);
            EsreCode? entity = all.FirstOrDefault(t => t.Id == id);
            if (entity is null)
                return ServiceResult<EsreCodeDto>.NotFound($"eSRE code {id} not found.");

            string code = Normalize(dto.Code);
            if (all.Any(t => t.Id != id && t.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                return ServiceResult<EsreCodeDto>.Conflict(
                    $"eSRE code '{code}' already exists.");

            var oldSnapshot = new { entity.Code, entity.Name, entity.IsActive };

            entity.Code = code;
            entity.Name = dto.Name.Trim();
            entity.Description = Blank(dto.Description);
            entity.IsActive = dto.IsActive;
            entity.UpdatedAt = DateTime.UtcNow;

            await _repo.UpdateAsync(entity, cancellationToken);
            await _repo.SaveChangesAsync(cancellationToken);
            await _audit.LogAsync("esre_codes", entity.Id, AuditAction.Update,
                oldValues: oldSnapshot,
                newValues: new { entity.Code, entity.Name, entity.IsActive },
                cancellationToken);
            return ServiceResult<EsreCodeDto>.Ok(MapToDto(entity));
        }

        // ── helpers ───────────────────────────────────────────────────────────────

        private static string? Validate(UpsertEsreCodeDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Code)) return "Code is required.";
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";

            return null;
        }

        private static string Normalize(string code) => code.Trim().ToUpperInvariant();

        private static string? Blank(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

        private static EsreCodeDto MapToDto(EsreCode t) =>
            new(t.Id, t.Code, t.Name, t.Description, t.IsActive);
    }
}