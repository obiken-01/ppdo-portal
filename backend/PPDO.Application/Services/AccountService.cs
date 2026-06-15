using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Chart of Accounts config CRUD + CSV upsert/export (RAL-70).
///
/// Expenditure type (PS/MOOE/CO) is derived from the account_number prefix — never stored:
///   5-01- = PS · 5-02- = MOOE · 5-03- = CO · other 5- = Other.
/// Soft delete only (IsActive = false). AccountNumber is the unique key.
/// The accounts table is small (~143 rows) — filtering/upsert happens in-memory.
/// </summary>
public sealed class AccountService : IAccountService
{
    private static readonly string[] CsvHeaders =
        { "account_title", "account_number", "normal_balance", "description", "is_active" };

    private readonly IRepository<Account> _repo;
    private readonly ILogger<AccountService> _logger;
    private readonly IAuditService _audit;

    public AccountService(IRepository<Account> repo, ILogger<AccountService> logger, IAuditService audit)
    {
        _repo   = repo;
        _logger = logger;
        _audit  = audit;
    }

    // ── Queries ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<AccountDto>> GetAllAsync(
        string? search, string? accountType, ActiveFilter active, CancellationToken cancellationToken = default)
    {
        IEnumerable<Account> q = await _repo.GetAllAsync(cancellationToken);

        q = active switch
        {
            ActiveFilter.Active   => q.Where(a => a.IsActive),
            ActiveFilter.Inactive => q.Where(a => !a.IsActive),
            _                     => q,
        };

        string? prefix = PrefixForType(accountType);
        if (prefix is not null)
            q = q.Where(a => a.AccountNumber.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(search))
        {
            string s = search.Trim();
            q = q.Where(a =>
                a.AccountNumber.Contains(s, StringComparison.OrdinalIgnoreCase) ||
                a.AccountTitle.Contains(s, StringComparison.OrdinalIgnoreCase));
        }

        return q.OrderBy(a => a.AccountNumber, StringComparer.OrdinalIgnoreCase)
                .Select(MapToDto)
                .ToList();
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AccountDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        Account? a = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(x => x.Id == id);
        return a is null
            ? ServiceResult<AccountDto>.NotFound($"Account {id} not found.")
            : ServiceResult<AccountDto>.Ok(MapToDto(a));
    }

    // ── Mutations ────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<ServiceResult<AccountDto>> CreateAsync(UpsertAccountDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.AccountTitle))
            return ServiceResult<AccountDto>.BadRequest("Account title is required.");
        if (string.IsNullOrWhiteSpace(dto.AccountNumber))
            return ServiceResult<AccountDto>.BadRequest("Account number is required.");

        string number = dto.AccountNumber.Trim();
        IReadOnlyList<Account> all = await _repo.GetAllAsync(cancellationToken);
        if (all.Any(a => a.AccountNumber.Equals(number, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<AccountDto>.Conflict($"Account number '{number}' already exists.");

        DateTime now = DateTime.UtcNow;
        Account entity = new()
        {
            AccountTitle  = dto.AccountTitle.Trim(),
            AccountNumber = number,
            NormalBalance = Blank(dto.NormalBalance),
            Description   = Blank(dto.Description),
            IsActive      = dto.IsActive,
            CreatedAt     = now,
            UpdatedAt     = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Account created. AccountNumber: {AccountNumber}", entity.AccountNumber);
        await _audit.LogAsync("accounts", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.AccountTitle, entity.AccountNumber, entity.IsActive },
            cancellationToken);
        return ServiceResult<AccountDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AccountDto>> UpdateAsync(int id, UpsertAccountDto dto, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dto.AccountTitle))
            return ServiceResult<AccountDto>.BadRequest("Account title is required.");
        if (string.IsNullOrWhiteSpace(dto.AccountNumber))
            return ServiceResult<AccountDto>.BadRequest("Account number is required.");

        IReadOnlyList<Account> all = await _repo.GetAllAsync(cancellationToken);
        Account? entity = all.FirstOrDefault(a => a.Id == id);
        if (entity is null)
            return ServiceResult<AccountDto>.NotFound($"Account {id} not found.");

        string number = dto.AccountNumber.Trim();
        if (all.Any(a => a.Id != id && a.AccountNumber.Equals(number, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<AccountDto>.Conflict($"Account number '{number}' already exists.");

        var oldSnapshot = new { entity.AccountTitle, entity.AccountNumber, entity.IsActive };

        entity.AccountTitle  = dto.AccountTitle.Trim();
        entity.AccountNumber = number;
        entity.NormalBalance = Blank(dto.NormalBalance);
        entity.Description   = Blank(dto.Description);
        entity.IsActive      = dto.IsActive;
        entity.UpdatedAt     = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("accounts", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.AccountTitle, entity.AccountNumber, entity.IsActive },
            cancellationToken);
        return ServiceResult<AccountDto>.Ok(MapToDto(entity));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AccountDto>> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        Account? entity = (await _repo.GetAllAsync(cancellationToken)).FirstOrDefault(a => a.Id == id);
        if (entity is null)
            return ServiceResult<AccountDto>.NotFound($"Account {id} not found.");

        entity.IsActive  = false;             // soft delete only — never hard-delete a referenced account
        entity.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Account deactivated. AccountNumber: {AccountNumber}", entity.AccountNumber);
        await _audit.LogAsync("accounts", entity.Id, AuditAction.Delete,
            oldValues: new { IsActive = true },
            newValues: null,
            cancellationToken);
        return ServiceResult<AccountDto>.Ok(MapToDto(entity));
    }

    // ── CSV ──────────────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public async Task<string> ExportCsvAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<Account> all = await _repo.GetAllAsync(cancellationToken);
        IEnumerable<string?[]> rows = all
            .OrderBy(a => a.AccountNumber, StringComparer.OrdinalIgnoreCase)
            .Select(a => new string?[]
            {
                a.AccountTitle, a.AccountNumber, a.NormalBalance, a.Description, a.IsActive ? "true" : "false",
            });
        return Csv.Write(CsvHeaders, rows);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default)
    {
        List<string[]> parsed = Csv.Parse(csvText);
        if (parsed.Count == 0)
            return ServiceResult<CsvImportResult>.BadRequest("The CSV file is empty.");

        int start = LooksLikeHeader(parsed[0], "account_number") ? 1 : 0;

        List<Account> all = (await _repo.GetAllAsync(cancellationToken)).ToList();
        Dictionary<string, Account> byNumber = all.ToDictionary(
            a => a.AccountNumber.Trim(), a => a, StringComparer.OrdinalIgnoreCase);

        int created = 0, updated = 0, skipped = 0;
        List<string> errors = new();
        DateTime now = DateTime.UtcNow;

        for (int i = start; i < parsed.Count; i++)
        {
            string[] f = parsed[i];
            string title  = Field(f, 0);
            string number = Field(f, 1).Trim();
            string nb     = Field(f, 2);
            string desc   = Field(f, 3);
            bool   active = Csv.ParseBool(Field(f, 4), fallback: true);

            if (number.Length == 0 || title.Trim().Length == 0)
            {
                skipped++;
                errors.Add($"Row {i + 1}: account_title and account_number are required.");
                continue;
            }

            if (byNumber.TryGetValue(number, out Account? existing))
            {
                bool changed =
                    existing.AccountTitle != title.Trim() ||
                    Blank(existing.NormalBalance) != Blank(nb) ||
                    Blank(existing.Description)   != Blank(desc) ||
                    existing.IsActive != active;

                if (!changed) { skipped++; continue; }

                existing.AccountTitle  = title.Trim();
                existing.NormalBalance = Blank(nb);
                existing.Description   = Blank(desc);
                existing.IsActive      = active;
                existing.UpdatedAt     = now;
                await _repo.UpdateAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                Account entity = new()
                {
                    AccountTitle  = title.Trim(),
                    AccountNumber = number,
                    NormalBalance = Blank(nb),
                    Description   = Blank(desc),
                    IsActive      = active,
                    CreatedAt     = now,
                    UpdatedAt     = now,
                };
                await _repo.AddAsync(entity, cancellationToken);
                byNumber[number] = entity;   // guard against duplicate keys within the same file
                created++;
            }
        }

        await _repo.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Accounts CSV imported. New: {New}, Updated: {Updated}, Skipped: {Skipped}", created, updated, skipped);
        return ServiceResult<CsvImportResult>.Ok(new CsvImportResult(created, updated, skipped, errors));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Maps PS/MOOE/CO (case-insensitive) to the account_number prefix; null otherwise.</summary>
    private static string? PrefixForType(string? accountType) => accountType?.Trim().ToUpperInvariant() switch
    {
        "PS"   => "5-01-",
        "MOOE" => "5-02-",
        "CO"   => "5-03-",
        _       => null,
    };

    /// <summary>Derives the expenditure type label from the account number prefix.</summary>
    private static string TypeOf(string accountNumber)
    {
        if (accountNumber.StartsWith("5-01-", StringComparison.OrdinalIgnoreCase)) return "PS";
        if (accountNumber.StartsWith("5-02-", StringComparison.OrdinalIgnoreCase)) return "MOOE";
        if (accountNumber.StartsWith("5-03-", StringComparison.OrdinalIgnoreCase)) return "CO";
        return "Other";
    }

    private static AccountDto MapToDto(Account a) =>
        new(a.Id, a.AccountTitle, a.AccountNumber, a.NormalBalance, a.Description, a.IsActive, TypeOf(a.AccountNumber));

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
