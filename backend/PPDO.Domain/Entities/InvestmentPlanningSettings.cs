namespace PPDO.Domain.Entities
{
    /// <summary>
    /// Province-wide Investment Planning settings (v1.8.0 — PPDO-136). A <b>single-row</b> table:
    /// the one row has <see cref="Id"/> = <see cref="SingletonId"/>, seeded by the migration, and a
    /// check constraint stops a second row ever being inserted.
    ///
    /// Typed columns rather than a key/value table on purpose — there is one value today, and a
    /// key/value store would turn a fiscal year into a string every reader has to parse. A second
    /// setting, when one is needed, is a second column.
    ///
    /// See <c>docs/v1.8/Default_Fiscal_Year_Spec.md</c>.
    /// </summary>
    public sealed class InvestmentPlanningSettings
    {
        /// <summary>The id of the only row.</summary>
        public const int SingletonId = 1;

        /// <summary>Primary key. Always <see cref="SingletonId"/>.</summary>
        public int Id { get; set; }

        /// <summary>
        /// The fiscal year every Investment Planning page opens on when its URL names none.
        /// <c>null</c> = unset: each page falls back to the default it used before this existed.
        /// </summary>
        public int? DefaultFiscalYear { get; set; }

        /// <summary>UTC time of the last change. Null until the setting is first saved.</summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Who made the last change. Null until first saved, or if that user is deleted.</summary>
        public Guid? UpdatedById { get; set; }

        public User? UpdatedBy { get; set; }
    }
}
