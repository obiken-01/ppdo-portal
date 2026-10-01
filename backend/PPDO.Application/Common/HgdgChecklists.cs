namespace PPDO.Application.Common;

/// <summary>
/// The "HGDG Checklist Used" picklist for Section A (Investment_Proposal_Spec.md decision 9).
///
/// <para>
/// ⚠️ <b>PROVISIONAL. Confirm against the PCW HGDG manual before PPDO-156 (T3) merges</b>, which
/// is the spec's open follow-up. None of the nine FY2027 samples fills this field, so they give no
/// list to copy. These are the HGDG design checklists as generally published: one generic and the
/// sector-specific boxes. The stored value is the <see cref="Code"/>. Codes are short and stable,
/// so renaming a label later never rewrites saved proposals; adding a missing checklist is a
/// one-line change here.
/// </para>
/// </summary>
public static class HgdgChecklists
{
    public sealed record Checklist(string Code, string Name);

    public static readonly IReadOnlyList<Checklist> All =
    [
        new("GENERIC",   "Generic checklist (design)"),
        new("AGRI",      "Agriculture and agrarian reform"),
        new("NRM",       "Natural resource management"),
        new("INFRA",     "Infrastructure"),
        new("PSD",       "Private sector development"),
        new("WMSE",      "Women's micro and small enterprises"),
        new("EDUC",      "Education"),
        new("HEALTH",    "Health"),
        new("JUSTICE",   "Justice"),
        new("MICROFIN",  "Microfinance"),
        new("LABOR",     "Labor and employment"),
        new("HOUSING",   "Housing and settlement"),
        new("MIGRATION", "Migration"),
        new("TOURISM",   "Tourism"),
        new("ICT",       "Information and communications technology"),
        new("VAW",       "Violence against women"),
        new("CHILDLAB",  "Child labor"),
    ];

    /// <summary>Whether <paramref name="code"/> is a known checklist. Case-sensitive: codes are stored as listed.</summary>
    public static bool IsKnown(string code) => All.Any(c => c.Code == code);
}
