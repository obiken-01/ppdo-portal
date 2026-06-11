namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: Chart of Accounts entry (Object of Expenditure).
/// Expenditure type (PS / MOOE / CO) is NOT stored — it is derived from the
/// AccountNumber prefix at query time: 5-01-xx = PS, 5-02-xx = MOOE, 5-03-xx = CO.
/// Soft delete via IsActive only — never hard-delete a referenced account.
/// Seeded manually via the Account Config page CSV upload (RAL-74).
/// </summary>
public sealed class Account
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>Object of Expenditure label. Max 300 characters.</summary>
    public string AccountTitle { get; set; } = string.Empty;

    /// <summary>Unique account number — e.g. "5-01-01-010". Max 20 characters.</summary>
    public string AccountNumber { get; set; } = string.Empty;

    /// <summary>"Debit" or "Credit". Optional. Max 10 characters.</summary>
    public string? NormalBalance { get; set; }

    /// <summary>Optional free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>Soft-delete flag. Inactive accounts are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
