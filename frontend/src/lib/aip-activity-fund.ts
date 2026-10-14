/**
 * The activity's funding sources as labelled, named entries for the Details block (PPDO-218).
 *
 * The header pill (`activityFundLabel`) shows bare codes and scrolls away in the review modal, so
 * a reader could not find the fund (Ralph, 2026-10-14). Same two sources, same order: the codes on
 * the expenditure lines for an entered year, else the uploaded snapshot for FY ≤ 2027.
 */

import type { FundingSourceResponse } from "@/types";

export function activityFundNames(
  activity: { fundCodes?: string[] | null; fundingSourceSnapshot?: string | null },
  funds: Pick<FundingSourceResponse, "code" | "name">[],
): string[] {
  const codes = activity.fundCodes ?? [];
  if (codes.length > 0) {
    return codes.map((code) => {
      const name = funds.find((f) => f.code === code)?.name;
      return name ? `${code} — ${name}` : code;
    });
  }
  const snapshot = activity.fundingSourceSnapshot?.trim();
  return snapshot ? [snapshot] : [];
}
