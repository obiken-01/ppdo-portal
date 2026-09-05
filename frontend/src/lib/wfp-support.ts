import { FIRST_OFFICE_OWNED_FISCAL_YEAR } from "@/lib/aip-shape";

/**
 * Which fiscal years this portal will build a WFP for, client side
 * (v1.8.0 Phase 3 — V18-81 / PPDO-49).
 *
 * Mirrors `PPDO.Application/Common/WfpSupportedYears`, including its shape: the break year is not
 * restated here, it is read from {@link FIRST_OFFICE_OWNED_FISCAL_YEAR}. That coupling is
 * deliberate — the reason FY2028 is uncertain is precisely that the AIP changes shape there, so if
 * the province ever slips the break, the WFP refusal must move with it in the same edit.
 * `AipShapeTests.TheBreakYear_IsHardcodedInExactlyOnePlace` scans this directory and fails the
 * build if the literal reappears.
 *
 * ⚠️ **This is a courtesy, not a guard.** `WfpService.SaveAsync` and `EnsureActivityAsync` each
 * refuse the year independently, as their first statement. Disabling a control only spares the
 * user a round trip that was always going to fail — never treat a check here as the reason
 * anything is safe.
 */

/**
 * Why a WFP cannot be built for `fiscalYear` in this portal, or `null` when it can.
 *
 * ⚠️ The wording says **"not yet"** on purpose. It is unknown whether the FY2028 WFP is built here
 * or in GSO's system (tracker C2), and the answer may well make this supported — a message reading
 * "not allowed" would tell the province something untrue about their own process.
 */
export function wfpUnsupportedReason(fiscalYear: number): string | null {
  if (fiscalYear < FIRST_OFFICE_OWNED_FISCAL_YEAR) return null;
  return (
    `FY ${fiscalYear} Work Financial Plans are not built in this portal yet. The FY ${fiscalYear} ` +
    `AIP is entered here, but its WFP is not — where that document is produced has not been ` +
    `settled. FY ${FIRST_OFFICE_OWNED_FISCAL_YEAR - 1} and earlier are unaffected.`
  );
}
