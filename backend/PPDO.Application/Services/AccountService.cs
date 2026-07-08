using Microsoft.Extensions.Logging;
using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;
using PPDO.Domain.Entities;
using PPDO.Domain.Interfaces;

namespace PPDO.Application.Services;

/// <summary>
/// Chart of Accounts config CRUD + CSV upsert/export (RAL-70, expanded RAL-117).
///
/// Expenditure type (PS/MOOE/CO) is stored in <c>expense_class</c> (RAL-117) — no longer
/// derived from the account_number prefix at read time, since the `1 07 xx` CO asset
/// accounts and similar exceptions break that rule. <c>default_nature</c> and
/// <c>default_apply_reserve</c> are optional, default-only pre-fills for WFP entry —
/// never enforced gates (see docs/v1.4/WFP_Rework_Requirements_Draft.md §5.3/§6).
/// Soft delete only (IsActive = false). AccountNumber is the unique key.
/// The accounts table is small (~300 rows) — filtering/upsert happens in-memory.
/// </summary>
public sealed class AccountService : IAccountService
{
    private static readonly string[] CsvHeaders =
    {
        "account_title", "account_number", "normal_balance", "description", "is_active",
        "expense_class", "default_nature", "default_apply_reserve",
    };

    /// <summary>The only 3 values <c>default_nature</c> may hold (case-insensitive on input, canonicalized on save).</summary>
    private static readonly string[] AllowedNatures = { "Procurement", "Non-Procurement", "Combined" };

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

        if (!string.IsNullOrWhiteSpace(accountType))
            q = q.Where(a => a.ExpenseClass.Equals(accountType.Trim(), StringComparison.OrdinalIgnoreCase));

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
        if (string.IsNullOrWhiteSpace(dto.ExpenseClass))
            return ServiceResult<AccountDto>.BadRequest("Expense class is required.");
        if (!TryCanonicalizeNature(dto.DefaultNature, out string? nature, out string? natureError))
            return ServiceResult<AccountDto>.BadRequest(natureError!);

        string number = dto.AccountNumber.Trim();
        IReadOnlyList<Account> all = await _repo.GetAllAsync(cancellationToken);
        if (all.Any(a => a.AccountNumber.Equals(number, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<AccountDto>.Conflict($"Account number '{number}' already exists.");

        DateTime now = DateTime.UtcNow;
        Account entity = new()
        {
            AccountTitle        = dto.AccountTitle.Trim(),
            AccountNumber       = number,
            NormalBalance       = Blank(dto.NormalBalance),
            Description         = Blank(dto.Description),
            IsActive            = dto.IsActive,
            ExpenseClass        = dto.ExpenseClass.Trim(),
            DefaultNature       = nature,
            DefaultApplyReserve = dto.DefaultApplyReserve,
            CreatedAt           = now,
            UpdatedAt           = now,
        };

        await _repo.AddAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Account created. AccountNumber: {AccountNumber}", entity.AccountNumber);
        await _audit.LogAsync("accounts", entity.Id, AuditAction.Create,
            oldValues: null,
            newValues: new { entity.AccountTitle, entity.AccountNumber, entity.IsActive, entity.ExpenseClass },
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
        if (string.IsNullOrWhiteSpace(dto.ExpenseClass))
            return ServiceResult<AccountDto>.BadRequest("Expense class is required.");
        if (!TryCanonicalizeNature(dto.DefaultNature, out string? nature, out string? natureError))
            return ServiceResult<AccountDto>.BadRequest(natureError!);

        IReadOnlyList<Account> all = await _repo.GetAllAsync(cancellationToken);
        Account? entity = all.FirstOrDefault(a => a.Id == id);
        if (entity is null)
            return ServiceResult<AccountDto>.NotFound($"Account {id} not found.");

        string number = dto.AccountNumber.Trim();
        if (all.Any(a => a.Id != id && a.AccountNumber.Equals(number, StringComparison.OrdinalIgnoreCase)))
            return ServiceResult<AccountDto>.Conflict($"Account number '{number}' already exists.");

        var oldSnapshot = new { entity.AccountTitle, entity.AccountNumber, entity.IsActive, entity.ExpenseClass };

        entity.AccountTitle        = dto.AccountTitle.Trim();
        entity.AccountNumber       = number;
        entity.NormalBalance       = Blank(dto.NormalBalance);
        entity.Description         = Blank(dto.Description);
        entity.IsActive            = dto.IsActive;
        entity.ExpenseClass        = dto.ExpenseClass.Trim();
        entity.DefaultNature       = nature;
        entity.DefaultApplyReserve = dto.DefaultApplyReserve;
        entity.UpdatedAt           = DateTime.UtcNow;

        await _repo.UpdateAsync(entity, cancellationToken);
        await _repo.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync("accounts", entity.Id, AuditAction.Update,
            oldValues: oldSnapshot,
            newValues: new { entity.AccountTitle, entity.AccountNumber, entity.IsActive, entity.ExpenseClass },
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
                a.ExpenseClass, a.DefaultNature, a.DefaultApplyReserve ? "true" : "false",
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

            // expense_class: fall back to the pre-RAL-117 prefix rule so a CSV exported
            // before this ticket (no expense_class column) still imports cleanly.
            string expenseClassCell = Field(f, 5).Trim();
            string expenseClass = expenseClassCell.Length > 0 ? expenseClassCell : DeriveExpenseClassFromPrefix(number);

            string natureCell = Field(f, 6);
            if (!TryCanonicalizeNature(Blank(natureCell), out string? nature, out string? natureError))
            {
                skipped++;
                errors.Add($"Row {i + 1}: {natureError}");
                continue;
            }

            bool applyReserve = Csv.ParseBool(Field(f, 7), fallback: false);

            if (byNumber.TryGetValue(number, out Account? existing))
            {
                bool changed =
                    existing.AccountTitle != title.Trim() ||
                    Blank(existing.NormalBalance) != Blank(nb) ||
                    Blank(existing.Description)   != Blank(desc) ||
                    existing.IsActive != active ||
                    existing.ExpenseClass != expenseClass ||
                    existing.DefaultNature != nature ||
                    existing.DefaultApplyReserve != applyReserve;

                if (!changed) { skipped++; continue; }

                existing.AccountTitle        = title.Trim();
                existing.NormalBalance       = Blank(nb);
                existing.Description         = Blank(desc);
                existing.IsActive            = active;
                existing.ExpenseClass        = expenseClass;
                existing.DefaultNature       = nature;
                existing.DefaultApplyReserve = applyReserve;
                existing.UpdatedAt           = now;
                await _repo.UpdateAsync(existing, cancellationToken);
                updated++;
            }
            else
            {
                Account entity = new()
                {
                    AccountTitle        = title.Trim(),
                    AccountNumber       = number,
                    NormalBalance       = Blank(nb),
                    Description         = Blank(desc),
                    IsActive            = active,
                    ExpenseClass        = expenseClass,
                    DefaultNature       = nature,
                    DefaultApplyReserve = applyReserve,
                    CreatedAt           = now,
                    UpdatedAt           = now,
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

    /// <summary>
    /// Pre-RAL-117 prefix rule, used only as a CSV-import fallback when a row's
    /// expense_class cell is blank (e.g. a pre-v1.4 CSV export). Never used at read time.
    /// </summary>
    private static string DeriveExpenseClassFromPrefix(string accountNumber)
    {
        if (accountNumber.StartsWith("5-01-", StringComparison.OrdinalIgnoreCase)) return "PS";
        if (accountNumber.StartsWith("5-02-", StringComparison.OrdinalIgnoreCase)) return "MOOE";
        if (accountNumber.StartsWith("5-03-", StringComparison.OrdinalIgnoreCase)) return "CO";
        return "Other";
    }

    /// <summary>
    /// Validates and canonicalizes default_nature: null/blank is accepted (no default),
    /// otherwise it must case-insensitively match one of <see cref="AllowedNatures"/>.
    /// </summary>
    private static bool TryCanonicalizeNature(string? input, out string? canonical, out string? error)
    {
        string? trimmed = Blank(input);
        if (trimmed is null)
        {
            canonical = null;
            error = null;
            return true;
        }

        string? match = AllowedNatures.FirstOrDefault(n => n.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        if (match is null)
        {
            canonical = null;
            error = $"default_nature must be one of: {string.Join(", ", AllowedNatures)}.";
            return false;
        }

        canonical = match;
        error = null;
        return true;
    }

    private static AccountDto MapToDto(Account a) =>
        new(a.Id, a.AccountTitle, a.AccountNumber, a.NormalBalance, a.Description, a.IsActive,
            AccountType: a.ExpenseClass, ExpenseClass: a.ExpenseClass,
            DefaultNature: a.DefaultNature, DefaultApplyReserve: a.DefaultApplyReserve);

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
