namespace PPDO.Domain.Entities;

/// <summary>
/// Maps an AIP Program to a Division for budget-planning assignment (v1.2 — RAL-99).
///
/// <b>Assignment is permanent, not per fiscal year</b> (confirmed 2026-09-01, RAL-249). There is
/// deliberately no fiscal year on this row: one assignment serves every FY whose program carries
/// the same ref code. <c>AllocationService.GetProgramAssignmentsAsync</c> resolves the FY's AIP
/// record, then looks the assignment up by ref code.
///
/// <b>⚠️ Why the program side is still keyed by ref code, and must stay that way.</b>
/// <c>aip_programs</c> rows have no identity that survives a fiscal year or a re-upload:
/// <c>AipService.ReplaceImportAsync</c> deletes the record's top-level <c>AipOffice</c> rows and
/// lets the cascade wipe the subtree, so every re-upload issues fresh surrogate IDs. An FK to
/// <c>aip_programs.id</c> would therefore do one of two harmful things — pin the assignment to a
/// single fiscal year (contradicting the rule above), or detach it on the next FY≤2027 re-upload.
/// Do not "finish the job" by adding one. RAL-249 explored this and stopped here on purpose.
///
/// <b>The office side IS a real FK</b> (<see cref="OfficeId"/>, RAL-249). A config
/// <c>offices</c> row is stable across fiscal years and re-uploads, so nothing argued for a
/// string there — it was only ever incidental to the program key. <see cref="OfficeRefCode"/>
/// is kept alongside it as the AIP-side re-link key and as the backfill audit trail; it is no
/// longer what reads match on.
/// </summary>
public sealed class ProgramDivision
{
    public int    Id              { get; set; }

    /// <summary>
    /// <c>AipOffice.RefCode</c> of the containing office row — e.g. <c>1000-000-1-01-010</c>.
    /// Retained after RAL-249 as the AIP-side re-link key (a re-upload recreates the AIP
    /// hierarchy; this is what re-attaches assignments to it) and as the record of what
    /// <see cref="OfficeId"/> was backfilled from. Reads match on <see cref="OfficeId"/>.
    /// </summary>
    public string OfficeRefCode  { get; set; } = string.Empty;

    /// <summary>
    /// FK to the config <c>offices</c> row (RAL-249). Nullable only because the backfill cannot
    /// guarantee a match for every legacy row — an unmatched row keeps a null here and is
    /// reported rather than dropped. New rows always set it.
    /// </summary>
    public int?   OfficeId       { get; set; }

    /// <summary>AipProgram.RefCode of the assigned program.</summary>
    public string ProgramRefCode { get; set; } = string.Empty;

    public int    DivisionId     { get; set; }

    public Division? Division { get; set; }

    public Office?   Office   { get; set; }
}
