"use client";

/**
 * OfficeSelect — office-specific preset on top of the generic Lookup combobox
 * (RAL-60 follow-up). Replaces the raw <select> that budget-planning/allocation/
 * wfp/admin-users each hand-rolled independently, which had drifted: unsorted
 * lists, a direct axios call bypassing @/lib/config on the users page, and
 * inconsistent label order.
 *
 * Bakes in the office-specific rules so call sites stay simple:
 *   - Sort: always by officeRefCode (zero-padded segment string compare, e.g.
 *     "01-001" before "03-010"); offices without a ref code sort last by officeCode.
 *   - Row content: "officeCode — officeName" (ref code is sort-only, not shown).
 *   - Search: matches officeCode or officeName.
 *
 * Caller supplies the already-fetched `offices` list (via listOffices() from
 * @/lib/config) — this component only sorts/renders, it does not fetch.
 */

import { useMemo } from "react";
import Lookup from "./Lookup";
import type { OfficeResponse } from "@/types";

export interface OfficeSelectProps {
  offices: OfficeResponse[];
  value: number | null;
  onChange: (officeId: number | null) => void;
  /** Label for the "no office selected" row, e.g. "All Offices". Omit to require a pick. */
  allOptionLabel?: string;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
}

function sortByRefCode(offices: OfficeResponse[]): OfficeResponse[] {
  return [...offices].sort((a, b) => {
    if (a.officeRefCode && b.officeRefCode) return a.officeRefCode.localeCompare(b.officeRefCode);
    if (a.officeRefCode) return -1;
    if (b.officeRefCode) return 1;
    return a.officeCode.localeCompare(b.officeCode);
  });
}

function officeLabel(o: OfficeResponse): string {
  return `${o.officeCode} — ${o.officeName}`;
}

export default function OfficeSelect({
  offices,
  value,
  onChange,
  allOptionLabel,
  placeholder = "Search office…",
  disabled,
  className,
}: OfficeSelectProps) {
  const sorted = useMemo(() => sortByRefCode(offices), [offices]);

  return (
    <Lookup<OfficeResponse>
      items={sorted}
      value={value}
      onChange={onChange}
      getId={(o) => o.id}
      getLabel={officeLabel}
      getSearchText={(o) => `${o.officeCode} ${o.officeName}`}
      allOptionLabel={allOptionLabel}
      placeholder={placeholder}
      disabled={disabled}
      className={className}
    />
  );
}
