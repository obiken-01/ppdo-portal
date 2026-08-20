using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Configurable division CRUD + CSV upsert/export (v1.2 — RAL-97 list, RAL-98 full CRUD).
/// Soft delete only. Name is the upsert key within an office; Code is optional/nullable.
/// </summary>
public sealed class DivisionService : IDivisionService
{
    private static readonly string[] CsvHeaders =
    {
        "office_code", "code", "name", "is_active",
        "can_access_budget_planning", "can_access_inventory", "can_access_reports",
        "can_manage_config", "can_upload_aip", "can_manage_users", "can_manage_resource_links",
    };

    private readonly IRepository<Division>   _divisions;
    private readonly IRepository<Office>     _offices;
    private readonly ILogger<DivisionService> _logger;
    private readonly IAuditService            _audit;

    public DivisionService(
        IRepository<Division> divisions,
        IRepository<Office>   offices,
        ILogger<DivisionService> logger,
        IAuditService audit)
    {
        _divisions = divisions;
        _offices   = offices;
        _logger    = logger;
        _audit     = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DivisionDto>> GetAllAsync(
        bool? activeOnly = null,
        int? officeId = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Division> divisions = await _divisions.GetAllAsync(cancellationToken);
        IReadOnlyList<Office> offices     = await _offices.GetAllAsync(cancellationToken);
        Dictionary<int, string> officeNames = offices.ToDictionary(o => o.Id, o => o.OfficeName);
        Dictionary<int, string> officeCodes = offices.ToDictionary(o => o.Id, o => o.OfficeCode);

        IEnumerable<Division> query = divisions;
        if (activeOnly == true)  query = query.Where(d => d.IsActive);
        if (activeOnly == false) query = query.Where(d => !d.IsActive);
        if (officeId is int oid) query = query.Where(d => d.OfficeId == oid);

        return query
            .OrderBy(d => d.OfficeId)
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => MapToDto(d, officeNames, officeCodes))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<DivisionDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Division> all = await _divisions.GetAllAsync(cancellationToken);
        Division? division = all.FirstOrDefault(d => d.Id == id);
        if (division is null)
            return ServiceResult<DivisionDto>.NotFound($"Division {id} not found.");

        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Dictionary<int, string> names  = offices.ToDictionary(o => o.Id, o => o.OfficeName);
        Dictionary<int, string> codes  = offices.ToDictionary(o => o.Id, o => o.OfficeCode);
        return ServiceResult<DivisionDto>.Ok(MapToDto(division, names, codes));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<DivisionDto>> CreateAsync(
        UpsertDivisionDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<DivisionDto>.BadRequest("Division name is required.");

        string name = dto.Name.Trim();
        IReadOnlyList<Division> all = await _divisions.GetAllAsync(cancellationToken);

        if (all.Any(d => d.OfficeId == dto.OfficeId
                      && d.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<DivisionDto>.Conflict($"A division named '{name}' already exists in this office.");

        // Code is the CSV upsert key (RAL-239), so it has to be unique within the office —
        // enforced by IX_divisions_office_id_code. Check here for a clean 409 instead of a
        // DbUpdateException.
        string? newCode = NullIfBlank(dto.Code);
        if (newCode is not null
            && all.Any(d => d.OfficeId == dto.OfficeId
                         && d.Code is not null
                         && d.Code.Equals(newCode, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<DivisionDto>.Conflict($"A division with code '{newCode}' already exists in this office.");

        DateTime now = DateTime.UtcNow;
        Division entity = new()
        {
            OfficeId                = dto.OfficeId,
            Code                    = newCode,
            Name                    = name,
            IsActive                = dto.IsActive,
            CanAccessBudgetPlanning = dto.CanAccessBudgetPlanning,
            CanAccessInventory      = dto.CanAccessInventory,
            CanAccessReports        = dto.CanAccessReports,
            CanManageConfig         = dto.CanManageConfig,
            CanUploadAip            = dto.CanUploadAip,
            CanManageUsers          = dto.CanManageUsers,
            CanManageResourceLinks  = dto.CanManageResourceLinks,
            CreatedAt               = now,
            UpdatedAt               = now,
        };

        await _divisions.AddAsync(entity, cancellationToken);
        await _divisions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Division created. OfficeId: {OfficeId}, Name: {Name}", entity.OfficeId, entity.Name);
        await _audit.LogAsync("divisions", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: AuditSnapshot(entity),
            cancellationToken);

        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Dictionary<int, string> officeNames = offices.ToDictionary(o => o.Id, o => o.OfficeName);
        Dictionary<int, string> officeCodes = offices.ToDictionary(o => o.Id, o => o.OfficeCode);
        return ServiceResult<DivisionDto>.Ok(MapToDto(entity, officeNames, officeCodes));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<DivisionDto>> UpdateAsync(
        int id, UpsertDivisionDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return ServiceResult<DivisionDto>.BadRequest("Division name is required.");

        IReadOnlyList<Division> all = await _divisions.GetAllAsync(cancellationToken);
        Division? entity = all.FirstOrDefault(d => d.Id == id);
        if (entity is null)
            return ServiceResult<DivisionDto>.NotFound($"Division {id} not found.");

        object oldSnapshot = AuditSnapshot(entity);

        // Name is editable as of RAL-239 — nothing persists a division name as text (every
        // referencing table carries division_id), so a rename propagates on the next read.
        // Both keys still have to stay unique within the office.
        string newName = dto.Name.Trim();
        if (all.Any(d => d.Id != id
                      && d.OfficeId == entity.OfficeId
                      && d.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<DivisionDto>.Conflict($"A division named '{newName}' already exists in this office.");

        string? newCode = NullIfBlank(dto.Code);
        if (newCode is not null
            && all.Any(d => d.Id != id
                         && d.OfficeId == entity.OfficeId
                         && d.Code is not null
                         && d.Code.Equals(newCode, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<DivisionDto>.Conflict($"A division with code '{newCode}' already exists in this office.");

        entity.Name                    = newName;
        entity.Code                    = newCode;
        entity.IsActive                = dto.IsActive;
        entity.CanAccessBudgetPlanning = dto.CanAccessBudgetPlanning;
        entity.CanAccessInventory      = dto.CanAccessInventory;
        entity.CanAccessReports        = dto.CanAccessReports;
        entity.CanManageConfig         = dto.CanManageConfig;
        entity.CanUploadAip            = dto.CanUploadAip;
        entity.CanManageUsers          = dto.CanManageUsers;
        entity.CanManageResourceLinks  = dto.CanManageResourceLinks;
        entity.UpdatedAt               = DateTime.UtcNow;

        await _divisions.UpdateAsync(entity, cancellationToken);
        await _divisions.SaveChangesAsync(cancellationToken);

        await _audit.LogAsync("divisions", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: AuditSnapshot(entity),
            cancellationToken);

        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Dictionary<int, string> officeNames = offices.ToDictionary(o => o.Id, o => o.OfficeName);
        Dictionary<int, string> officeCodes = offices.ToDictionary(o => o.Id, o => o.OfficeCode);
        return ServiceResult<DivisionDto>.Ok(MapToDto(entity, officeNames, officeCodes));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<DivisionDto>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Division> all = await _divisions.GetAllAsync(cancellationToken);
        Division? entity = all.FirstOrDefault(d => d.Id == id);
        if (entity is null)
            return ServiceResult<DivisionDto>.NotFound($"Division {id} not found.");

        entity.IsActive  = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await _divisions.UpdateAsync(entity, cancellationToken);
        await _divisions.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Division deactivated. Id: {Id}, Name: {Name}", entity.Id, entity.Name);
        await _audit.LogAsync("divisions", entity.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);

        IReadOnlyList<Office> offices = await _offices.GetAllAsync(cancellationToken);
        Dictionary<int, string> officeNames = offices.ToDictionary(o => o.Id, o => o.OfficeName);
        Dictionary<int, string> officeCodes = offices.ToDictionary(o => o.Id, o => o.OfficeCode);
        return ServiceResult<DivisionDto>.Ok(MapToDto(entity, officeNames, officeCodes));
    }

    /// <inheritdoc />
    public async Task<string> ExportCsvAsync(
        IReadOnlyList<Office> offices, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Division> all = await _divisions.GetAllAsync(cancellationToken);
        Dictionary<int, string> codes = offices.ToDictionary(o => o.Id, o => o.OfficeCode);

        IEnumerable<string?[]> rows = all
            .OrderBy(d => codes.GetValueOrDefault(d.OfficeId, ""))
            .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .Select(d => new string?[]
            {
                codes.GetValueOrDefault(d.OfficeId, ""),
                d.Code ?? "",
                d.Name,
                d.IsActive ? "TRUE" : "FALSE",
                d.CanAccessBudgetPlanning ? "TRUE" : "FALSE",
                d.CanAccessInventory       ? "TRUE" : "FALSE",
                d.CanAccessReports         ? "TRUE" : "FALSE",
                d.CanManageConfig          ? "TRUE" : "FALSE",
                d.CanUploadAip             ? "TRUE" : "FALSE",
                d.CanManageUsers           ? "TRUE" : "FALSE",
                d.CanManageResourceLinks   ? "TRUE" : "FALSE",
            });
        return Csv.Write(CsvHeaders, rows);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CsvImportResult>> ImportCsvAsync(
        string csvText, IReadOnlyList<Office> offices, CancellationToken cancellationToken = default)
    {
        List<string[]> parsed = Csv.Parse(csvText);
        if (parsed.Count == 0)
            return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

        int start = parsed[0].Any(c => c.Trim().Equals("office_code", StringComparison.OrdinalIgnoreCase)) ? 1 : 0;

        Dictionary<string, int> officeCodeToId = offices.ToDictionary(
            o => o.OfficeCode.Trim(), o => o.Id, StringComparer.OrdinalIgnoreCase);

        List<Division> all = (await _divisions.GetAllAsync(cancellationToken)).ToList();

        // Upsert key is (officeId, code) whenever the row carries one — that is what lets a
        // division be renamed in place instead of the re-seed creating a duplicate (RAL-239).
        // Codeless rows keep the pre-RAL-239 (officeId, name) behaviour and cannot be renamed
        // this way; give the division a code to make it renameable.
        Dictionary<(int, string), Division> byCode = all
            .Where(d => !string.IsNullOrWhiteSpace(d.Code))
            .ToDictionary(d => (d.OfficeId, d.Code!.Trim().ToLowerInvariant()));
        Dictionary<(int, string), Division> byName = all.ToDictionary(
            d => (d.OfficeId, d.Name.Trim().ToLowerInvariant()));

        int created = 0, updated = 0, skipped = 0;
        List<string> errors = new();
        DateTime now = DateTime.UtcNow;
        // Names and codes are deduped separately: two rows may legitimately share neither,
        // but either colliding inside one file would breach a unique index on save.
        HashSet<(int, string)> processedNames = new();
        HashSet<(int, string)> processedCodes = new();

        for (int i = start; i < parsed.Count; i++)
        {
            string[] f = parsed[i];
            string officeCode = Field(f, 0).Trim();
            string? code      = NullIfBlank(Field(f, 1));
            string name       = Field(f, 2).Trim();
            bool   active     = Csv.ParseBool(Field(f, 3), fallback: true);
            bool   budget     = Csv.ParseBool(Field(f, 4), fallback: false);
            bool   inventory  = Csv.ParseBool(Field(f, 5), fallback: false);
            bool   reports    = Csv.ParseBool(Field(f, 6), fallback: false);
            bool   config     = Csv.ParseBool(Field(f, 7), fallback: false);
            bool   uploadAip  = Csv.ParseBool(Field(f, 8), fallback: false);
            bool   manageUsers = Csv.ParseBool(Field(f, 9), fallback: false);
            bool   resourceLinks = Csv.ParseBool(Field(f, 10), fallback: false);

            if (officeCode.Length == 0 || name.Length == 0)
            {
                skipped++;
                errors.Add($"Row {i + 1}: office_code and name are required.");
                continue;
            }

            if (!officeCodeToId.TryGetValue(officeCode, out int officeId))
            {
                skipped++;
                errors.Add($"Row {i + 1}: office_code '{officeCode}' not found.");
                continue;
            }

            (int, string) nameKey = (officeId, name.ToLowerInvariant());
            (int, string)? codeKey = code is null ? null : (officeId, code.ToLowerInvariant());

            if (!processedNames.Add(nameKey))
            {
                skipped++;
                errors.Add($"Row {i + 1}: duplicate name '{name}' for office '{officeCode}' in this file.");
                continue;
            }

            if (codeKey is not null && !processedCodes.Add(codeKey.Value))
            {
                skipped++;
                errors.Add($"Row {i + 1}: duplicate code '{code}' for office '{officeCode}' in this file.");
                continue;
            }

            // Match on code first, then fall back to name for codeless rows.
            Division? existing = null;
            if (codeKey is not null)
                byCode.TryGetValue(codeKey.Value, out existing);
            existing ??= byName.GetValueOrDefault(nameKey);

            // A rename must not land on a name another division already holds, and adopting a
            // code must not steal one — either would breach a unique index at SaveChanges.
            if (existing is not null
                && byName.TryGetValue(nameKey, out Division? nameOwner)
                && nameOwner.Id != existing.Id)
            {
                skipped++;
                errors.Add($"Row {i + 1}: cannot rename '{existing.Name}' to '{name}' — another division in office '{officeCode}' already uses that name.");
                continue;
            }

            if (existing is not null && codeKey is not null
                && byCode.TryGetValue(codeKey.Value, out Division? codeOwner)
                && codeOwner.Id != existing.Id)
            {
                skipped++;
                errors.Add($"Row {i + 1}: code '{code}' already belongs to division '{codeOwner.Name}' in office '{officeCode}'.");
                continue;
            }

            if (existing is not null)
            {
                bool changed =
                    existing.Code                    != code        ||
                    !existing.Name.Equals(name, StringComparison.Ordinal) ||
                    existing.IsActive                != active      ||
                    existing.CanAccessBudgetPlanning != budget      ||
                    existing.CanAccessInventory      != inventory   ||
                    existing.CanAccessReports        != reports     ||
                    existing.CanManageConfig         != config      ||
                    existing.CanUploadAip            != uploadAip   ||
                    existing.CanManageUsers          != manageUsers ||
                    existing.CanManageResourceLinks  != resourceLinks;

                if (!changed) { skipped++; continue; }

                // Keep the lookups in step with the rename/recode, so a later row in the same
                // file resolves against current state rather than the pre-import names.
                byName.Remove((existing.OfficeId, existing.Name.Trim().ToLowerInvariant()));
                if (!string.IsNullOrWhiteSpace(existing.Code))
                    byCode.Remove((existing.OfficeId, existing.Code.Trim().ToLowerInvariant()));

                existing.Code                    = code;
                existing.Name                    = name;
                existing.IsActive                = active;
                existing.CanAccessBudgetPlanning = budget;
                existing.CanAccessInventory      = inventory;
                existing.CanAccessReports        = reports;
                existing.CanManageConfig         = config;
                existing.CanUploadAip            = uploadAip;
                existing.CanManageUsers          = manageUsers;
                existing.CanManageResourceLinks  = resourceLinks;
                existing.UpdatedAt               = now;

                byName[nameKey] = existing;
                if (codeKey is not null) byCode[codeKey.Value] = existing;

                await _divisions.UpdateAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                Division entity = new()
                {
                    OfficeId                = officeId,
                    Code                    = code,
                    Name                    = name,
                    IsActive                = active,
                    CanAccessBudgetPlanning = budget,
                    CanAccessInventory      = inventory,
                    CanAccessReports        = reports,
                    CanManageConfig         = config,
                    CanUploadAip            = uploadAip,
                    CanManageUsers          = manageUsers,
                    CanManageResourceLinks  = resourceLinks,
                    CreatedAt               = now,
                    UpdatedAt               = now,
                };
                await _divisions.AddAsync(entity, cancellationToken);
                byName[nameKey] = entity;
                if (codeKey is not null) byCode[codeKey.Value] = entity;
                created++;
            }
        }

        await _divisions.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Divisions CSV imported. New: {New}, Updated: {Updated}, Skipped: {Skipped}", created, updated, skipped);
        return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped, errors));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static DivisionDto MapToDto(
        Division d,
        Dictionary<int, string> officeNames,
        Dictionary<int, string>? officeCodes = null) =>
        new(d.Id, d.OfficeId,
            officeNames.GetValueOrDefault(d.OfficeId),
            officeCodes?.GetValueOrDefault(d.OfficeId),
            d.Code, d.Name, d.IsActive,
            d.CanAccessInventory,
            d.CanAccessReports,
            d.CanManageUsers,
            d.CanManageResourceLinks,
            d.CanAccessBudgetPlanning,
            d.CanUploadAip,
            d.CanManageConfig);

    private static object AuditSnapshot(Division d) => new
    {
        d.OfficeId, d.Code, d.Name, d.IsActive,
        d.CanAccessBudgetPlanning, d.CanAccessInventory, d.CanAccessReports,
        d.CanManageConfig, d.CanUploadAip, d.CanManageUsers, d.CanManageResourceLinks,
    };

    private static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
