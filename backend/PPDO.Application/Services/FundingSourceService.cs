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
///
/// <b>v1.8.0 (PPDO-109):</b> a row with no office is province-wide, one with an office belongs to
/// that office alone (D5). Reads take an office to be visible to; see
/// <see cref="IFundingSourceService.GetAllAsync"/> for the rule. The usage counts behind the delete
/// guard DO go to SQL — <c>wfp_expenditures</c> and <c>aip_expenditures</c> are leaf tables that
/// grow per line per activity per office per year, and are never loaded in memory.
/// </summary>
public sealed class FundingSourceService : IFundingSourceService
{
    // office_code is EXPORT-ONLY and last on purpose (PPDO-109). It tells an operator whose fund a
    // row is, and the import below deliberately does not read it back — see ImportCsvAsync.
    private static readonly string[] CsvHeaders =
        { "code", "name", "description", "color", "is_active", "aliases", "office_code" };

    private readonly IRepository<FundingSource>     _repo;
    private readonly IRepository<Office>             _officeRepo;
    private readonly IWfpExpenditureRepository       _wfpExpRepo;
    private readonly IAipExpenditureRepository       _aipExpRepo;
    private readonly ILogger<FundingSourceService>   _logger;
    private readonly IAuditService                   _audit;

    public FundingSourceService(
        IRepository<FundingSource>   repo,
        IRepository<Office>          officeRepo,
        IWfpExpenditureRepository    wfpExpRepo,
        IAipExpenditureRepository    aipExpRepo,
        ILogger<FundingSourceService> logger,
        IAuditService                audit)
    {
        _repo       = repo;
        _officeRepo = officeRepo;
        _wfpExpRepo = wfpExpRepo;
        _aipExpRepo = aipExpRepo;
        _logger     = logger;
        _audit      = audit;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FundingSourceDto>> GetAllAsync(
        string? search, ActiveFilter active, int? visibleToOfficeId = null,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<FundingSource> q = await _repo.GetAllAsync(cancellationToken);

        // PPDO-109 — shared rows PLUS the one office's own. Null asks for no filter at all, which
        // is the config manager's cross-office view; every other caller arrives here already
        // clamped to an office they may read.
        if (visibleToOfficeId is int officeId)
            q = q.Where(f => f.OfficeId is null || f.OfficeId == officeId);

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

        // Shared funds first, then the office's own — the order the config page renders, so the
        // read-only rows a department head cannot touch are not interleaved with theirs.
        List<FundingSource> rows = q
            .OrderBy(f => f.OfficeId is null ? 0 : 1)
            .ThenBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IReadOnlyDictionary<int, Office> offices = await OfficesByIdAsync(rows, cancellationToken);
        return rows.Select(f => MapToDto(f, offices)).ToList();
    }

    /// <summary>
    /// The offices named by <paramref name="rows"/>, for labelling whose fund each one is. Skips
    /// the query entirely when every row is shared, which is the state of the table today.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, Office>> OfficesByIdAsync(
        IReadOnlyCollection<FundingSource> rows, CancellationToken cancellationToken)
    {
        if (!rows.Any(f => f.OfficeId is not null))
            return new Dictionary<int, Office>();

        return (await _officeRepo.GetAllAsync(cancellationToken))
            .GroupBy(o => o.Id)
            .ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>One fund's office label, resolved on its own — the single-row form of the map above.</summary>
    private async Task<FundingSourceDto> MapWithOfficeAsync(FundingSource f, CancellationToken cancellationToken)
        => MapToDto(f, await OfficesByIdAsync(new[] { f }, cancellationToken));

    /// <inheritdoc />
    public async Task<ServiceResult<FundingSourceDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        FundingSource? f = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
        return f is null
            ? ServiceResult<FundingSourceDto>.NotFound($"Funding source {id} not found.")
            : ServiceResult<FundingSourceDto>.Ok(await MapWithOfficeAsync(f, cancellationToken));
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
        // ⚠️ Uniqueness is checked across EVERY row, not within the office (PPDO-109, D6) — so an
        // office adding "GF" is told the code is taken, which is the behaviour §3.4 asks for.
        if (all.Any(f => f.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<FundingSourceDto>.Conflict($"Funding source code '{code}' already exists.");

        if (dto.OfficeId is int newOfficeId
            && (await _officeRepo.GetAllAsync(cancellationToken)).All(o => o.Id != newOfficeId))
            return ServiceResult<FundingSourceDto>.BadRequest($"Office {newOfficeId} not found.");

        DateTime now = DateTime.UtcNow;
        FundingSource entity = new()
        {
            Code        = code,
            Name        = dto.Name.Trim(),
            Description = Blank(dto.Description),
            Color       = Blank(dto.Color),
            Aliases     = Blank(dto.Aliases),
            OfficeId    = dto.OfficeId,   // null = province-wide (PPDO-109, D5)
            IsActive    = dto.IsActive,
            CreatedAt   = now,
            UpdatedAt   = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Funding source created. Code: {Code}, OfficeId: {OfficeId}", entity.Code, entity.OfficeId);
        await _audit.LogAsync("funding_sources", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.Code, entity.Name, entity.OfficeId, entity.IsActive },
            cancellationToken);
        return ServiceResult<FundingSourceDto>.Ok(await MapWithOfficeAsync(entity, cancellationToken));
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

        // ── Ownership (PPDO-128) ──────────────────────────────────────────────
        // ↩️ OfficeId IS read from the body now. PPDO-109 fixed ownership at creation; Demo 2 asked
        // for PPDO to be able to flip a fund between shared and office-owned. The body is trusted
        // here because the handler has already pinned an office-scoped caller's value to their own
        // office — only a config manager's choice reaches this line unaltered.
        int? oldOfficeId = entity.OfficeId;
        int? newOfficeId = dto.OfficeId;

        if (newOfficeId != oldOfficeId)
        {
            // BadRequest, as in CreateAsync — the office is a bad value in the body, not the
            // resource the request is addressed to.
            if (newOfficeId is int targetOfficeId
                && (await _officeRepo.GetAllAsync(cancellationToken)).All(o => o.Id != targetOfficeId))
                return ServiceResult<FundingSourceDto>.BadRequest($"Office {targetOfficeId} not found.");

            // ⚠️ Only NARROWING is guarded — shared → an office, or one office → another. That hides
            // the fund from offices that may already have lines under it, orphaning their budget
            // data from their own pickers. Widening (→ shared) hides nothing, so it is never refused.
            //
            // ↩️ Only usage by OTHER offices counts. The target office's own lines keep seeing the
            // fund, so they are no reason to stop — and counting them made the expected setup
            // (only General Fund shared, every other fund limited to the office that uses it)
            // impossible for any fund already in use.
            //
            // ↩️ And it WARNS rather than refuses (Ralph, 2026-09-24). Nearly all such usage is the
            // FY2027 uploaded AIP, which names a fund on every office's activities — history that
            // prints from its stored code either way. So an unconfirmed change is refused with the
            // per-year breakdown, and a confirmed one proceeds. What the other offices lose: the fund
            // leaves their pickers, and re-saving one of their activities that names it is refused
            // until another fund is picked.
            //
            // ⚠️ Ceilings and division allocations also name a fund and are NOT counted.
            if (newOfficeId is int target)
            {
                FundOwnershipImpactDto impact = await OtherOfficeUsageAsync(id, target, cancellationToken);

                if (impact.OtherOfficeLines > 0 && !dto.ConfirmOwnershipChange)
                {
                    _logger.LogWarning(
                        "Funding source ownership change needs confirmation — used by other offices. Code: {Code}, OldOfficeId: {OldOfficeId}, NewOfficeId: {NewOfficeId}, Rows: {Rows}",
                        entity.Code, oldOfficeId, newOfficeId, impact.OtherOfficeLines);
                    return ServiceResult<FundingSourceDto>.Conflict(
                        $"{entity.Name} ({entity.Code}) is used by {impact.OtherOfficeLines} AIP/WFP " +
                        $"{(impact.OtherOfficeLines == 1 ? "line" : "lines")} in other offices " +
                        $"({DescribeYears(impact)}). Limiting it to one office takes it out of theirs; " +
                        "confirm the change to go ahead.");
                }

                if (impact.OtherOfficeLines > 0)
                    _logger.LogWarning(
                        "Funding source limited to one office despite other offices' usage (confirmed). Code: {Code}, OldOfficeId: {OldOfficeId}, NewOfficeId: {NewOfficeId}, Rows: {Rows}",
                        entity.Code, oldOfficeId, newOfficeId, impact.OtherOfficeLines);
            }
        }

        var oldSnapshot = new { entity.Code, entity.Name, entity.OfficeId, entity.IsActive };

        entity.Code        = code;
        entity.Name        = dto.Name.Trim();
        entity.Description = Blank(dto.Description);
        entity.Color       = Blank(dto.Color);
        entity.Aliases     = Blank(dto.Aliases);
        entity.IsActive    = dto.IsActive;
        entity.UpdatedAt   = DateTime.UtcNow;
        entity.OfficeId    = newOfficeId;   // null = shared (PPDO-109, D5)

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        if (newOfficeId != oldOfficeId)
            _logger.LogInformation(
                "Funding source ownership changed. Code: {Code}, OldOfficeId: {OldOfficeId}, NewOfficeId: {NewOfficeId}",
                entity.Code, oldOfficeId, newOfficeId);

        await _audit.LogAsync("funding_sources", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.Code, entity.Name, entity.OfficeId, entity.IsActive },
            cancellationToken);
        return ServiceResult<FundingSourceDto>.Ok(await MapWithOfficeAsync(entity, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<FundOwnershipImpactDto>> GetOwnershipImpactAsync(
        int id, int? targetOfficeId, CancellationToken cancellationToken = default)
    {
        FundingSource? entity = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(f => f.Id == id);
        if (entity is null)
            return ServiceResult<FundOwnershipImpactDto>.NotFound($"Funding source {id} not found.");

        // Widening, or no change: nobody new loses the fund, so there is nothing to count.
        if (targetOfficeId is not int target || target == entity.OfficeId)
            return ServiceResult<FundOwnershipImpactDto>.Ok(new FundOwnershipImpactDto(0, []));

        return ServiceResult<FundOwnershipImpactDto>.Ok(await OtherOfficeUsageAsync(id, target, cancellationToken));
    }

    /// <summary>
    /// AIP + WFP usage of the fund by every office except <paramref name="officeId"/>, merged per
    /// fiscal year and ordered oldest first. Shared by the preview and the update guard so the two
    /// can never disagree about a count.
    /// </summary>
    private async Task<FundOwnershipImpactDto> OtherOfficeUsageAsync(int id, int officeId, CancellationToken ct)
    {
        // Sequential, not Task.WhenAll — one DbContext, which is not thread-safe (CLAUDE.md).
        IReadOnlyDictionary<int, int> wfp = await _wfpExpRepo.CountByFundingSourceOutsideOfficeAsync(id, officeId, ct);
        IReadOnlyDictionary<int, int> aip = await _aipExpRepo.CountByFundingSourceOutsideOfficeAsync(id, officeId, ct);

        List<FundUsageYearDto> byYear = wfp.Concat(aip)
            .GroupBy(kv => kv.Key)
            .Select(g => new FundUsageYearDto(g.Key, g.Sum(kv => kv.Value)))
            .OrderBy(y => y.FiscalYear)
            .ToList();
        return new FundOwnershipImpactDto(byYear.Sum(y => y.Lines), byYear);
    }

    /// <summary>"FY2027: 145, FY2028: 1" — the split the confirmation turns on.</summary>
    private static string DescribeYears(FundOwnershipImpactDto impact)
        => string.Join(", ", impact.ByFiscalYear.Select(y => $"FY{y.FiscalYear}: {y.Lines}"));

    /// <inheritdoc />
    public async Task<ServiceResult<FundingSourceDto>> DeleteAsync(
        int id, bool blockWhenInUse = false, CancellationToken cancellationToken = default)
    {
        FundingSource? entity = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(f => f.Id == id);
        if (entity is null)
            return ServiceResult<FundingSourceDto>.NotFound($"Funding source {id} not found.");

        if (blockWhenInUse)
        {
            // Sequential, not Task.WhenAll — one DbContext, which is not thread-safe (CLAUDE.md).
            int wfpRows = await _wfpExpRepo.CountByFundingSourceAsync(id, cancellationToken);
            int aipRows = await _aipExpRepo.CountByFundingSourceAsync(id, cancellationToken);
            int inUse   = wfpRows + aipRows;

            if (inUse > 0)
            {
                _logger.LogWarning(
                    "Funding source deactivation blocked — still in use. Code: {Code}, Rows: {Rows}",
                    entity.Code, inUse);
                return ServiceResult<FundingSourceDto>.Conflict(
                    $"{entity.Name} ({entity.Code}) is used by {inUse} AIP/WFP " +
                    $"{(inUse == 1 ? "line" : "lines")} and cannot be removed. " +
                    "Clear those lines first, or ask PPDO to retire the fund.");
            }
        }

        entity.IsActive  = false;
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Funding source deactivated. Code: {Code}", entity.Code);
        await _audit.LogAsync("funding_sources", entity.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);
        return ServiceResult<FundingSourceDto>.Ok(await MapWithOfficeAsync(entity, cancellationToken));
    }

    /// <inheritdoc />
    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<FundingSource> all = await _repo.GetAllAsync(cancellationToken);
        IReadOnlyDictionary<int, Office> offices = await OfficesByIdAsync(all, cancellationToken);
        IEnumerable<string?[]> rows = all
            .OrderBy(f => f.Code, StringComparer.OrdinalIgnoreCase)
            .Select(f => new string?[]
            {
                f.Code, f.Name, f.Description, f.Color, f.IsActive ? "true" : "false", f.Aliases,
                f.OfficeId is int oid && offices.TryGetValue(oid, out Office? o) ? o.OfficeCode : null,
            });
        return Csv.Write(CsvHeaders, rows);
    }

    /// <inheritdoc />
    ///
    /// <remarks>
    /// ⚠️ <b><c>office_code</c> is exported but never imported (PPDO-109).</b> Ownership is not
    /// something a spreadsheet round-trip should be able to change: an existing row KEEPS the office
    /// it has, and a new row is created province-wide. Reading the column back would mean a
    /// department head's fund silently became PPDO's — or another office's — the next time anyone
    /// re-uploaded an export taken before it existed. This route is config-manager-only for the same
    /// family of reasons (see <c>ConfigFundingSourceFunctions</c>).
    /// </remarks>
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
            string code    = Field(f, 0).Trim();
            string name    = Field(f, 1);
            string desc    = Field(f, 2);
            string color   = Field(f, 3);
            bool   active  = Csv.ParseBool(Field(f, 4), fallback: true);
            string aliases = Field(f, 5);

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
                    Blank(existing.Color) != Blank(color) ||
                    Blank(existing.Aliases) != Blank(aliases) ||
                    existing.IsActive != active;

                if (!changed) { skipped++; continue; }

                existing.Name        = name.Trim();
                existing.Description = Blank(desc);
                existing.Color       = Blank(color);
                existing.Aliases     = Blank(aliases);
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
                    Color       = Blank(color),
                    Aliases     = Blank(aliases),
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

    private static FundingSourceDto MapToDto(FundingSource f, IReadOnlyDictionary<int, Office> offices)
    {
        Office? office = f.OfficeId is int oid && offices.TryGetValue(oid, out Office? o) ? o : null;
        return new(f.Id, f.Code, f.Name, f.Description, f.Color, f.IsActive, f.Aliases,
                   f.OfficeId, office?.OfficeCode, office?.OfficeName);
    }

    private static string? Blank(string? value)
    {
        string t = (value ?? string.Empty).Trim();
        return t.Length == 0 ? null : t;
    }

    private static string Field(string[] row, int index) => index < row.Length ? row[index] : string.Empty;
}
