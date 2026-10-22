namespace PPDO.Domain.Entities;

/// <summary>
/// One line of an approved SAIP's before → after list (v1.10.0 — <c>docs/v1.10/SAIP_Spec.md</c> §5,
/// decisions 11 and 17). Written once, at approval, by the same diff the live "changes so far" panel
/// uses. The table is created by PPDO-246 (T1) and filled by T3.
/// </summary>
public sealed class AipSaipChange
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>FK to the SAIP.</summary>
    public int SaipId { get; set; }

    /// <summary><c>project</c>, <c>activity</c>, <c>expenditure</c> or <c>item</c>. Max 20 characters.</summary>
    public string NodeType { get; set; } = string.Empty;

    /// <summary>The approved row's id after the change was applied.</summary>
    public int NodeId { get; set; }

    /// <summary><c>added</c>, <c>changed</c> or <c>reduced to zero</c>. Max 20 characters.</summary>
    public string Change { get; set; } = string.Empty;

    /// <summary>The field that changed, null for an added row. Max 100 characters.</summary>
    public string? Field { get; set; }

    /// <summary>The value before, as text. Null for an added row.</summary>
    public string? Before { get; set; }

    /// <summary>The value after, as text.</summary>
    public string? After { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    public AipSaip? Saip { get; set; }
}
