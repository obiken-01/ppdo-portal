namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: Chart of Accounts entry (Object of Expenditure).
/// Expenditure type (PS / MOOE / CO) is stored in <see cref="ExpenseClass"/> (RAL-117) —
/// no longer derived from the AccountNumber prefix at query time, since the
/// `1 07 xx` CO asset accounts and other exceptions break the prefix rule.
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

    /// <summary>
    /// Expenditure class — e.g. "PS", "MOOE", "CO". Stored, not derived (RAL-117): the
    /// old 5-01-/5-02-/5-03- prefix rule breaks for the `1 07 xx` CO asset accounts.
    /// Required. Max 20 characters.
    /// </summary>
    public string ExpenseClass { get; set; } = string.Empty;

    /// <summary>
    /// Default-only pre-fill for the WFP expenditure "Nature" field: "Procurement",
    /// "Non-Procurement", or "Combined". Nullable — no value means the account has no
    /// default and the user must choose explicitly. Never an enforced gate. Max 20 characters.
    /// </summary>
    public string? DefaultNature { get; set; }

    /// <summary>
    /// Default-only pre-fill for the WFP expenditure "Reserve" toggle. Every account may
    /// have the toggle turned on regardless of this flag — it is never an enforced
    /// eligibility gate. Default false.
    /// </summary>
    public bool DefaultApplyReserve { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
