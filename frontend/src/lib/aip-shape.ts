/**
 * The AIP fiscal-year partition, client side (v1.8.0 Phase 2 — V18-38 / PPDO-41).
 *
 * Mirrors `PPDO.Application/Common/AipShape` and exists for the same reason: the break year is
 * written **once**, so moving it — should the province ever slip FY2028 — is one edit per side
 * rather than a hunt through pages. `AipShapeTests.TheBreakYear_IsHardcodedInExactlyOnePlace`
 * scans this directory too and fails the build if the literal reappears anywhere else.
 *
 * ⚠️ **This is a courtesy, not a guard.** Every refusal here is enforced independently by the
 * server; disabling a control only spares the user a round trip that was always going to fail.
 * Never treat a check in this file as the reason something is safe.
 */

/**
 * The first fiscal year on the office-owned AIP shape. FY2027 is the last year on the legacy
 * multi-office shape — this names the first NEW year, not the last old one.
 */
export const FIRST_OFFICE_OWNED_FISCAL_YEAR = 2028;

/**
 * Why an `.xlsm` upload may not target `fiscalYear`, or `null` when it may.
 *
 * The importer builds one record spanning every office in the province, which is the shape only
 * historical years use. From the break onward the AIP is **entered** in the portal instead, so the
 * message names the year and the alternative rather than reading as a validation failure — the
 * user has the permission, the year does not have the shape.
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
  if (fiscalYear < FIRST_OFFICE_OWNED_FISCAL_YEAR) return null;
  return (
    `FY ${fiscalYear} AIP programs come from this office's LDIP and cannot be typed in. Use ` +
    `“Seed from LDIP” to add the programs you need. If a program is missing there, add it to the ` +
    `LDIP first — the AIP cannot contain a program the LDIP does not.`
  );
}

export function aipUploadRefusal(fiscalYear: number): string | null {
  if (fiscalYear < FIRST_OFFICE_OWNED_FISCAL_YEAR) return null;
  return (
    `FY ${fiscalYear} AIPs are entered in the portal, not uploaded. The .xlsm import builds one ` +
    `record spanning every office, which is the shape only FY ${FIRST_OFFICE_OWNED_FISCAL_YEAR - 1} ` +
    `and earlier use. Use the Manual Entry tab to start the FY ${fiscalYear} AIP instead.`
  );
}
