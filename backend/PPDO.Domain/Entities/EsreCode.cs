namespace PPDO.Domain.Entities
{
    /// <summary>
    /// Config table: an eSRE classification code used to tag an AIP activity (v1.8.0 — RAL-248).
    ///
    /// A <b>closed vocabulary of four</b> — SS, ES, ID, EN — confirmed against the province's
    /// FY2027 file in <c>docs/v1.8/AIP_Form_Spec.md</c> §3.1. Because it is closed, the four rows
    /// are seeded literally by the migration, unlike <see cref="ClimateChangeTypology"/> whose
    /// codes are open-ended and derived from the imported data.
    ///
    /// <b>Why this table exists.</b> <c>AipActivity.EsreCode</c> was a free string, and one row in
    /// 2,357 reads "PPDO/PEO" — an implementing-office name typed into the eSRE column. One bad
    /// value is a low error rate, but it is exactly the error a pick-list makes impossible, and it
    /// is the evidence behind this ticket. That row is deliberately left orphaned for the Phase 2
    /// backfill to flag rather than being seeded in and legitimised.
    ///
    /// Soft delete via <see cref="IsActive"/> only — never hard-delete a code an activity
    /// references. An AIP is an audited document; a code that vanishes makes a historical
    /// activity unreadable.
    /// </summary>
    public sealed class EsreCode
    {
        /// <summary>Primary key (INT IDENTITY).</summary>
        public int Id { get; set; }

        /// <summary>The eSRE code — SS, ES, ID or EN. Unique, stored upper-case. Max 20 chars.</summary>
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Display name — "Social Services", "Economic Services", "Institutional Development",
        /// "Environmental Services". This is what makes the table worth having: without it the
        /// row is a lookup that says SS = SS, and the picker cannot label its options.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Optional free-text description.</summary>
        public string? Description { get; set; }

        /// <summary>Soft-delete flag. Inactive codes are hidden from pickers but kept for history.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}