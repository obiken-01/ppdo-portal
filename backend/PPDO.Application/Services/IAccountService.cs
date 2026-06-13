using PPDO.Application.Common;
using PPDO.Application.DTOs.Config;

namespace PPDO.Application.Services;

/// <summary>
/// Chart of Accounts config CRUD + CSV upsert/export (RAL-70).
/// Soft delete only (IsActive = false). AccountNumber is the unique key.
/// </summary>
public interface IAccountService
{
    /// <summary>
    /// Lists accounts ordered by account number.
    /// <paramref name="search"/> matches account number OR title (case-insensitive, contains).
    /// <paramref name="accountType"/> "PS"/"MOOE"/"CO" filters by account_number prefix
    /// (5-01-/5-02-/5-03-); null = no type filter.
    /// </summary>
    Task<IReadOnlyList<AccountDto>> GetAllAsync(
        string? search, string? accountType, ActiveFilter active, CancellationToken cancellationToken = default);

    Task<ServiceResult<AccountDto>> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ServiceResult<AccountDto>> CreateAsync(UpsertAccountDto dto, CancellationToken cancellationToken = default);
    Task<ServiceResult<AccountDto>> UpdateAsync(int id, UpsertAccountDto dto, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes (IsActive = false). Returns the updated record.</summary>
    Task<ServiceResult<AccountDto>> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Exports all accounts as CSV: account_title, account_number, normal_balance, description, is_active.</summary>
    Task<string> ExportCsvAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts accounts by account_number. Returns new/updated/skipped counts.</summary>
    Task<ServiceResult<CsvImportResult>> ImportCsvAsync(string csvText, CancellationToken cancellationToken = default);
}
