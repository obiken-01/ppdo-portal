using PPDO.Domain.Enums;

namespace PPDO.Domain.Entities;

/// <summary>
/// Config table: a provincial government office (e.g. PPDO, PGO).
/// Soft delete via IsActive only — never hard-delete a referenced office.
/// Seeded manually via the Office Config page CSV upload (RAL-72).
/// </summary>
public sealed class Office
{
    /// <summary>Primary key (INT IDENTITY).</summary>
    public int Id { get; set; }

    /// <summary>Short unique office code — e.g. "PPDO", "PGO". Max 20 characters.</summary>
    public string OfficeCode { get; set; } = string.Empty;

    /// <summary>Full office name. Max 200 characters.</summary>
    public string OfficeName { get; set; } = string.Empty;

    /// <summary>
    /// Last segment of the office-level AIP ref code (e.g. "013" from "3000-000-1-01-013").
    /// Used to match this config office to the correct AIP office hierarchy row.
    /// Nullable — populated manually via CSV download/upload.
    /// </summary>
    public string? OfficeRefCode { get; set; }

    /// <summary>Soft-delete flag. Inactive offices are hidden from pickers but kept for history.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Marks the office that hosts the portal — PPDO today (DECISION F, RAL-258).
    ///
    /// This is the single discriminator for cross-office authority. Its users see every office's
    /// data; every other office's users are clamped to their own. It replaces two older mechanisms
    /// that nothing kept in agreement: <c>users.office_id IS NULL</c> meaning "PPDO person", and
    /// three hardcoded <c>"PPDO"</c> string lookups meaning "PPDO row".
    ///
    /// Named for what it governs rather than who currently holds it, so it survives the office
    /// being renamed or restructured.
    ///
    /// <b>Exactly one row may be true</b> — enforced by a filtered unique index
    /// (<c>UX_offices_is_host_office</c>), not by application code.
    /// </summary>
    public bool IsHostOffice { get; set; }


    /// <summary>
    /// Default landing page for every user in this office (RAL-251). Null = no preference; the
    /// resolver falls through to the next level of the chain
    /// (user → division → office → first permitted → Profile).
    /// </summary>
    /// <remarks>
    /// Not permission-checked on write, unlike the per-user preference: one office default is
    /// shared by users whose overrides differ, so a value that is unreachable for some of them is
    /// legitimate. <c>LandingPageResolver</c> skips it per-user at read time (RAL-258).
    /// </remarks>
    public LandingPage? LandingPage { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────

    /// <summary>WFP records scoped to this office.</summary>
    public ICollection<WfpRecord> WfpRecords { get; set; } = new List<WfpRecord>();

    /// <summary>Non-PPDO users belonging to this office (encoders / viewers). Added in RAL-81.</summary>
    public ICollection<User> Users { get; set; } = new List<User>();
}
