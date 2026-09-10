/**
 * The fiscal year at which the AIP changes PROCESS, client side (v1.8.0 — V18-38, V18-41).
 *
 * ↩️ Was `aip-shape.ts`, renamed 2026-09-05 (PPDO-61) along with the record-shape partition it
 * mirrored. There are no longer two record shapes: one base AIP record per fiscal year holds every
 * office. What survived is the year — from FY2028 the AIP is entered rather than uploaded.
 *
 * Mirrors `PPDO.Application/Common/AipFiscalYears` and exists for the same reason: the break year is
 * written **once**, so moving it — should the province ever slip FY2028 — is one edit per side
 * rather than a hunt through pages. `AipShapeTests.TheBreakYear_IsHardcodedInExactlyOnePlace`
 * scans this directory too and fails the build if the literal reappears anywhere else.
 *
 * ⚠️ **This is a courtesy, not a guard.** Every refusal here is enforced independently by the
 * server; disabling a control only spares the user a round trip that was always going to fail.
 * Never treat a check in this file as the reason something is safe.
 */

/**
 * The first fiscal year on the new process: entered, not uploaded. FY2027 is the last uploaded
 * year — this names the first NEW year, not the last old one.
 */
export const FIRST_ENTERED_FISCAL_YEAR = 2028;

/**
 * Why an `.xlsm` upload may not target `fiscalYear`, or `null` when it may.
 *
 * From the break year the AIP is **entered** by the offices rather than imported from a workbook,
 * so a file upload would overwrite what they have typed. The message names the year and the
 * alternative rather than reading as a validation failure — the user has the permission, it is the
 * fiscal year that forbids this.
 */
/**
 * Why an AIP program cannot be typed in by hand for `fiscalYear`, or `null` when it can
 * (V18-41 / PPDO-51).
 *
 * From the break year on the LDIP is a **closed list**: an office may only add programs its LDIP
 * already contains. There is no "propose a new program" path — if a program is missing, it is
 * missing from the LDIP, and that is where it has to be added.
 *
 * ⚠️ Courtesy, not a guard. `AipService.AddProgramAsync` refuses this independently.
 */
export function aipProgramsAreLdipOnly(fiscalYear: number): string | null {
  if (fiscalYear < FIRST_ENTERED_FISCAL_YEAR) return null;
  return (
    `FY ${fiscalYear} AIP programs come from this office's LDIP and cannot be typed in. Use ` +
    `“Seed from LDIP” to add the programs you need. If a program is missing there, add it to the ` +
    `LDIP first — the AIP cannot contain a program the LDIP does not.`
  );
}

export function aipUploadRefusal(fiscalYear: number): string | null {
  if (fiscalYear < FIRST_ENTERED_FISCAL_YEAR) return null;
  return (
    `FY ${fiscalYear} AIPs are entered in the portal, not uploaded. From FY ` +
    `${FIRST_ENTERED_FISCAL_YEAR} each office builds its own part of the AIP directly, so importing ` +
    `a workbook would overwrite what they have entered.`
  );
}
