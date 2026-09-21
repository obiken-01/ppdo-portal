namespace PPDO.Application.Common;

/// <summary>
/// The fiscal year at which the AIP changes <b>process</b>, and the refusals that follow from it
/// (v1.8.0 — V18-38, V18-41, V18-81).
///
/// <para>
/// ↩️ <b>Replaces <c>AipShape</c>, removed 2026-09-05 (PPDO-61).</b> That type partitioned the
/// record <i>structure</i> at FY2028 — legacy multi-office below, office-owned above — and
/// enforced it on four create paths. The office-owned shape is withdrawn:
/// <b>every fiscal year uses one base AIP record holding every office</b>
/// (<c>AIP_Foundation_Spec.md</c> §2 decision 4). There are no longer two shapes to tell apart, so
/// <c>Required</c>, <c>Of</c>, <c>Mismatch</c> and <c>RefuseForeignOffice</c> went with the
/// partition.
/// </para>
///
/// <para>
/// <b>What survived is the year itself, and it was always the more durable half.</b> FY2028 is
/// still a real break — just not a structural one. From that year the AIP is <b>entered in the
/// portal rather than uploaded</b> from a workbook, and its WFP is not built in this system at all.
/// Both of those refusals shipped and neither changes; only the reason they give did.
/// </para>
///
/// <para>
/// ⚠️ <b>The break year lives here and nowhere else.</b>
/// <c>AipFiscalYearsTests.TheBreakYear_IsHardcodedInExactlyOnePlace</c> scans the four production
/// projects <i>and</i> <c>frontend/src</c>, and fails the build if the literal reappears. Moving
/// the break — should the province ever slip FY2028 — must stay one edit per side rather than a
/// hunt.
/// </para>
/// </summary>
public static class AipFiscalYears
{
    /// <summary>
    /// The first fiscal year on the new process: <b>entered, not uploaded</b>, and with no WFP
    /// built in this portal.
    ///
    /// <para>
    /// ↩️ Was <c>AipShape.FirstOfficeOwnedFiscalYear</c>. Renamed with the shape it no longer
    /// names — a constant called "first office-owned year" in a codebase where nothing is
    /// office-owned is the kind of stale name that gets believed.
    /// </para>
    ///
    /// <para>
    /// ⚠️ This names the <b>first new</b> year, not the last old one. FY2027 is the last year that
    /// is uploaded and the last that has a WFP here.
    /// </para>
    /// </summary>
    public const int FirstEnteredFiscalYear = 2028;

    /// <summary>
    /// Whether <paramref name="fiscalYear"/> is on the new process. Prefer the specific refusals
    /// below at call sites — this exists for the few places that genuinely need the raw question.
    /// </summary>
    public static bool IsEntered(int fiscalYear) => fiscalYear >= FirstEnteredFiscalYear;

    /// <summary>
    /// Why an <c>.xlsm</c> import may not target <paramref name="fiscalYear"/>, or null when it
    /// may (V18-38 / PPDO-41).
    ///
    /// <para>
    /// ⚠️ <b>The reason changed on 2026-09-05; the behaviour did not.</b> This used to refuse
    /// because the importer could only build the legacy multi-office shape, which FY2028 was not
    /// allowed to have. FY2028 now uses exactly that shape, so the structural argument is gone —
    /// but the refusal stands on the ground it always really rested on: <b>from FY2028 the AIP is
    /// entered by the offices, not imported from a workbook.</b> A file upload would overwrite
    /// work the offices typed.
    /// </para>
    ///
    /// <para>
    /// ⚠️ <b>The parser is deliberately left alone.</b> FY≤2027 still imports through it, including
    /// the re-upload path, and freezing the years it may target is not the same as retiring it.
    /// </para>
    /// </summary>
    public static string? RefuseUpload(int fiscalYear)
        => IsEntered(fiscalYear)
            ? $"FY {fiscalYear} AIPs are entered in the portal, not uploaded. From FY "
              + $"{FirstEnteredFiscalYear} each office builds its own part of the AIP directly, so "
              + "importing a workbook would overwrite what they have entered. Uploading is still "
              + $"how FY {FirstEnteredFiscalYear - 1} and earlier were recorded."
            : null;
}
