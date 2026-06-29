namespace PPDO.Domain.Entities;

/// <summary>
/// Maps an AIP Program to a Division for budget-planning assignment (v1.2 — RAL-99).
///
/// Keyed by (OfficeRefCode, ProgramRefCode) — NOT by aip_programs.id — so that
/// assignments survive supplemental AIP re-uploads, which recreate aip_programs
/// rows with new surrogate IDs (D6). When a supplemental upload confirms, existing
/// ProgramDivision rows automatically re-link via ref-code matching in the service.
/// Genuinely new programs (new RefCode) appear as "unassigned" on the Allocation tab.
/// </summary>
public sealed class ProgramDivision
{
    public int    Id              { get; set; }

    /// <summary>AipOffice.RefCode of the containing office row.</summary>
    public string OfficeRefCode  { get; set; } = string.Empty;

    /// <summary>AipProgram.RefCode of the assigned program.</summary>
    public string ProgramRefCode { get; set; } = string.Empty;

    public int    DivisionId     { get; set; }

    public Division? Division { get; set; }
}
