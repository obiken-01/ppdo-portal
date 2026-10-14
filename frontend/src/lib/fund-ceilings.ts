/**
 * Ceilings on fund sources other than the General Fund (v1.9.0 — PPDO-217,
 * `docs/v1.9/Fund_Source_Ceiling_Spec.md`).
 *
 * A fund is held to a ceiling only when PPDO finance ticks "Enforce AIP ceiling" on it. The General
 * Fund always is. Every other fund stays out of sight on the ceiling surfaces (Ralph, 2026-10-14):
 * a tab for a fund nobody limits would invite ceilings nobody asked for, which is what D4 hid the
 * tab strip for in the first place.
 *
 * ⚠️ Courtesy only: the server enforces every one of these rules again (PPDO-216).
 */

import type { AipCeilingStatus, FundingSourceResponse } from "@/types";
import { findGeneralFund } from "./config";

/** The Office Ceilings tabs: the General Fund, then each enforced fund by code. Nothing else. */
export function ceilingFundTabs(funds: FundingSourceResponse[]): FundingSourceResponse[] {
  const gf = findGeneralFund(funds);
  const others = funds
    .filter((f) => f.enforceAipCeiling && f.id !== gf?.id)
    .sort((a, b) => a.code.localeCompare(b.code));
  return gf ? [gf, ...others] : others;
}

/**
 * The flag's part of a save body: present only when it changed. ⚠️ A department head editing their
 * own fund never changes it, so it is never sent for them, and the server's grant check (a 403 on a
 * change) can never be tripped by an ordinary edit.
 */
export function ceilingFlagPayload(original: boolean, edited: boolean): { enforceAipCeiling?: boolean } {
  return original === edited ? {} : { enforceAipCeiling: edited };
}

/**
 * Whether the checkbox can be changed, and the note shown when it cannot.
 *
 * @param fund the fund being edited, or null for a new one.
 * @param canManageOfficeCeilings the caller holds PPDO finance's office-ceiling grant.
 */
export function ceilingFlagControl(
  fund: FundingSourceResponse | null,
  canManageOfficeCeilings: boolean,
): { disabled: boolean; note: string | null } {
  if (fund && findGeneralFund([fund])) return { disabled: true, note: "Always enforced" };
  if (!canManageOfficeCeilings) {
    return { disabled: true, note: "Only PPDO finance (the office-ceiling grant) can change this." };
  }
  return { disabled: false, note: null };
}

/**
 * What an office has encoded against one fund's ceiling, for that fund's tab.
 *
 * The server lists an enforced fund in `otherFunds` only once the office has encoded on it or has a
 * ceiling for it, so a fund that is absent there has nothing encoded: zero, not unknown. Null only
 * when there is no AIP at all to compare against.
 */
export function encodedForFund(usage: AipCeilingStatus | null, fundId: number): number | null {
  if (!usage) return null;
  if (usage.generalFundId === fundId) return usage.encodedBaseRounded;
  return usage.otherFunds?.find((f) => f.fundingSourceId === fundId)?.encodedBaseRounded ?? 0;
}

/**
 * The offices a fund's tab lists. A province-wide fund lists every office; an office's OWN fund
 * (PPDO-109) lists only that office: no other office can encode on it, so a row for them could only
 * collect a ceiling that means nothing. Found while live-testing PPDO-217.
 */
export function officesForFundTab<T extends { officeId: number }>(
  offices: T[],
  fund: FundingSourceResponse | null,
): T[] {
  if (fund?.officeId == null) return offices;
  return offices.filter((o) => o.officeId === fund.officeId);
}
