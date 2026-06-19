namespace PPDO.Application.DTOs.Config;

/// <summary>
/// Read model for a Chart of Accounts entry. <see cref="AccountType"/> is derived from
/// the <see cref="AccountNumber"/> prefix (PS/MOOE/CO/Other) and is not stored.
/// </summary>
public sealed record AccountDto(
    int     Id,
    string  AccountTitle,
    string  AccountNumber,
    string? NormalBalance,
    string? Description,
    bool    IsActive,
    string  AccountType);

/// <summary>Create/update body for an account. AccountNumber is the unique key.</summary>
public sealed record UpsertAccountDto(
    string  AccountTitle,
    string  AccountNumber,
    string? NormalBalance,
    string? Description,
    bool    IsActive = true);
