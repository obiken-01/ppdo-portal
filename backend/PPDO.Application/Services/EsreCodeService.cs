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
        private readonly IEsreCodeRepository _repo;
        private readonly ILogger<EsreCodeService> _logger;
        private readonly IAuditService _audit;

        public EsreCodeService(
            IEsreCodeRepository repo,
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
        /// <inheritdoc />
        public async Task<int> GetCountAsync(
            string? search, ActiveFilter active, CancellationToken cancellationToken = default)
            // Pushed to SQL rather than counting GetAllAsync in memory. Four rows make the saving
            // trivial; the point is that this tile is the one the next config page copies.
            => await _repo.CountAsync(ToIsActive(active), search, cancellationToken);

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

        // ── CSV (PPDO-19) ─────────────────────────────────────────────────────────

        /// <summary>The export column order, and the order ImportCsvAsync reads fields in.</summary>
        private static readonly string[] CsvHeaders = ["code", "name", "description", "is_active"];

        /// <inheritdoc />
        public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<EsreCode> all = await _repo.GetAllAsync(cancellationToken);
            IEnumerable<string?[]> rows = all
                .OrderBy(t => t.Code, StringComparer.OrdinalIgnoreCase)
                .Select(t => new string?[] { t.Code, t.Name, t.Description, t.IsActive ? "true" : "false" });
            return Csv.Write(CsvHeaders, rows);
        }

        /// <inheritdoc />
        public async Task<ServiceResult<CsvImportResult>> ImportCsvAsync(
            string csvText, CancellationToken cancellationToken = default)
        {
            // Whitespace-only counts as empty. Csv.Parse would otherwise hand back one blank
            // row and the caller would get "Row 1: Code is required." for an empty file, which
            // describes the wrong problem. (A small, deliberate divergence from
            // FundingSourceService.ImportCsvAsync, which still has that behaviour.)
            if (string.IsNullOrWhiteSpace(csvText))
                return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

            List<string[]> parsed = Csv.Parse(csvText);
            if (parsed.Count == 0)
                return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

            int start = parsed[0].Any(c => c.Trim().Equals("code", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

            List<EsreCode> all = (await _repo.GetAllAsync(cancellationToken)).ToList();
            Dictionary<string, EsreCode> byCode = all.ToDictionary(
                t => t.Code.Trim(), t => t, StringComparer.OrdinalIgnoreCase);

            int created = 0, updated = 0, skipped = 0;
            List<string> errors = new();
            // Emitted AFTER SaveChangesAsync — a created row has no Id before then (PPDO-19).
            List<PendingAudit> audits = new();
            DateTime now = DateTime.UtcNow;

            for (int i = start; i < parsed.Count; i++)
            {
                string[] f    = parsed[i];
                string   code = Normalize(Field(f, 0));
                string   name = Field(f, 1).Trim();
                string?  desc = Blank(Field(f, 2));
                bool   active = Csv.ParseBool(Field(f, 3), fallback: true);

                string? invalid = Validate(new UpsertEsreCodeDto(code, name, desc, active));
                if (invalid is not null)
                {
                    skipped++;
                    errors.Add($"Row {i + 1}: {invalid}");
                    continue;
                }

                if (byCode.TryGetValue(code, out EsreCode? existing))
                {
                    bool changed =
                        existing.Name != name ||
                        Blank(existing.Description) != desc ||
                        existing.IsActive != active;

                    if (!changed) { skipped++; continue; }

                    audits.Add(new PendingAudit(existing, AuditAction.Update, Snapshot(existing)));

                    existing.Name        = name;
                    existing.Description = desc;
                    existing.IsActive    = active;
                    existing.UpdatedAt   = now;
                    await _repo.UpdateAsync(existing, cancellationToken);
                    updated++;
                }
                else
                {
                    // ⚠️ This branch can create a FIFTH eSRE code, on purpose — see
                    // IEsreCodeService.ImportCsvAsync. The vocabulary is closed today, not
                    // forever, and this is how a newly issued code gets in without a release.
                    // Do not "fix" this into a whitelist check.
                    EsreCode entity = new()
                    {
                        Code        = code,
                        Name        = name,
                        Description = desc,
                        IsActive    = active,
                        CreatedAt   = now,
                        UpdatedAt   = now,
                    };
                    await _repo.AddAsync(entity, cancellationToken);
                    byCode[code] = entity;
                    audits.Add(new PendingAudit(entity, AuditAction.Create, null));
                    created++;
                }
            }

            await _repo.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "eSRE codes CSV imported. New: {New}, Updated: {Updated}, Skipped: {Skipped}",
                created, updated, skipped);

            // One row per record actually changed. Skipped rows produce nothing.
            foreach (PendingAudit a in audits)
                await _audit.LogAsync("esre_codes", a.Entity.Id, a.Action,
                    a.OldValues, Snapshot(a.Entity), cancellationToken);

            return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped, errors));
        }

        /// <summary>An audit entry held until the entities have Ids. See ImportCsvAsync.</summary>
        private sealed record PendingAudit(EsreCode Entity, string Action, object? OldValues);

        private static object Snapshot(EsreCode t) =>
            new { t.Code, t.Name, t.Description, t.IsActive };

        private static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

        // ── helpers ───────────────────────────────────────────────────────────────

        private static string? Validate(UpsertEsreCodeDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Code)) return "Code is required.";
            if (string.IsNullOrWhiteSpace(dto.Name)) return "Name is required.";

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

        private static EsreCodeDto MapToDto(EsreCode t) =>
            new(t.Id, t.Code, t.Name, t.Description, t.IsActive);
    }
}