namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: a CCET (Climate Change Expenditure Tagging) typology code — e.g. "A113-08",
/// "M314-03" — used to tag an AIP activity's climate-change contribution (v1.8.0 — RAL-247).
///
/// Replaces the free string on <c>AipActivity.CcTypologyCode</c>, which is on a document that
/// gets audited and had no controlled vocabulary.
///
/// Soft delete via <see cref="IsActive"/> only — never hard-delete a code an activity references.
///
/// <b>⚠️ One activity can carry more than one code.</b> 18 of the 167 tagged FY2027 activities
/// hold two comma-separated codes in the single free-text field (e.g. "A222-03, A224-05"). This
/// table is therefore the vocabulary, not the assignment: the Phase 2 backfill that puts AIP
/// activities onto references needs a join table, NOT a single FK column on aip_activities.
/// Sizing that wrong is the one way this table's shape can cost work later.
/// </summary>
public sealed class ClimateChangeTypology
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>
    /// The CCET code — e.g. "A113-08". Unique. Max 20 characters.
    /// The leading letter carries <see cref="Category"/>: A = adaptation, M = mitigation.
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// Display name. Seeded to the code itself where the province's own label is not yet known —
    /// the codes exist in the FY2027 data, their official descriptions do not.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// "Adaptation" or "Mitigation", from the code's leading letter. Stored rather than derived
    /// so the picker can group without every caller re-parsing the code, and so a code that does
    /// not follow the letter convention can still be filed correctly by hand.
    /// </summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Optional free-text description.</summary>
    public string? Description { get; set; }

    /// <summary>Soft-delete flag. Inactive codes are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
