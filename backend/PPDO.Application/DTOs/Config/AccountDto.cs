namespace PPDO.Application.DTOs.Config;

/// <summary>
/// Read model for a Chart of Accounts entry. <see cref="AccountType"/> mirrors
/// <see cref="ExpenseClass"/> (RAL-117) — kept as a separate, same-shaped field so existing
/// WFP/AIP consumers reading <c>AccountType</c> are unaffected by the switch from
/// prefix-derivation to a stored column.
/// </summary>
public sealed record AccountDto(
    int     Id,
    string  AccountTitle,
    string  AccountNumber,
    string? NormalBalance,
    string? Description,
    bool    IsActive,
    string  AccountType,
    string  ExpenseClass,
    string? DefaultNature,
    bool    DefaultApplyReserve);

/// <summary>Create/update body for an account. AccountNumber is the unique key.</summary>
public sealed record UpsertAccountDto(
    string  AccountTitle,
    string  AccountNumber,
    string? NormalBalance,
    string? Description,
    bool    IsActive = true,
    string  ExpenseClass = "",
    string? DefaultNature = null,
    bool    DefaultApplyReserve = false);
