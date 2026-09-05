namespace PPDO.Application.Common;

/// <summary>
/// Where an AIP program is allowed to come from, per fiscal year
/// (v1.8.0 Phase 3 — V18-41 / PPDO-51).
///
/// <para>
/// <b>The LDIP is a closed list</b> (open question #5, answered 2026-08-25). From
/// <see cref="AipShape.FirstOfficeOwnedFiscalYear"/> on, an office may only add programs its LDIP
/// already contains — it cannot invent one. There is therefore <b>no "propose a new program" path
/// and no approval flow for one</b>: a branch the original plan anticipated, and that does not
/// exist. Anyone reading this expecting to build it has the wrong version.
/// </para>
///
/// <para>
/// FY≤2027 is <b>unchanged</b>. Those records were imported from a workbook that carries whatever
/// programs the province typed into it, and freely-named programs are how the manual-entry flow
/// has always worked there. Closing the free-typed path for historical years would break the one
/// working use, exactly as freezing the importer for them would have.
/// </para>
/// </summary>
public static class AipProgramSource
{
    /// <summary>
    /// Why a freely-named program may not be added in <paramref name="fiscalYear"/>, or null when
    /// it may.
    ///
    /// <para>
    /// The message names the LDIP as the source rather than merely refusing, because the person
    /// reading it has a program they intend to add and needs to know where it has to come from.
    /// If it is genuinely absent from their LDIP, the fix is in the LDIP — not here.
    /// </para>
    /// </summary>
    public static string? RefuseFreeTypedProgram(int fiscalYear)
        => fiscalYear >= AipShape.FirstOfficeOwnedFiscalYear
            ? $"FY {fiscalYear} AIP programs come from this office's LDIP and cannot be typed in. "
              + "Use “Seed from LDIP” to add the programs you need. If a program is missing "
              + "there, add it to the LDIP first — the AIP cannot contain a program the LDIP does "
              + "not."
            : null;
}
