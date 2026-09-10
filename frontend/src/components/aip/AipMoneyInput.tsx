"use client";

/**
 * A money input on an AIP surface: `MoneyInput` plus the live `= 1,234.57 ₱000` echo.
 *
 * ⚠️ **This exists so the unit is unambiguous at the point of typing.** AIP amounts are entered in
 * PESOS and displayed in THOUSANDS, so an input and the cell it saves into legitimately show
 * different numbers. Without the echo that reads as a bug — or worse, does not read as anything:
 * the archived FY2028 test data holds a line of ₱4,657,655,000 that someone typed as `4,657,655`
 * into a field they believed was pesos, and nothing on screen contradicted them.
 *
 * ⚠️ **One component, not a hint repeated at each call site.** There are seventeen of these across
 * the two AIP pages; a copied hint is a hint that goes stale in fifteen of them.
 *
 * ℹ️ `MoneyInput` already renders the `₱` prefix, so the input half needs nothing added.
 */

import MoneyInput from "@/components/ui/MoneyInput";
import { thousandsHint } from "@/lib/aip-units";

export default function AipMoneyInput({
  value, onChange, disabled, className,
}: {
  /** PESOS. Not thousands — see the header. */
  value: number | null;
  onChange: (value: number | null) => void;
  disabled?: boolean;
  className?: string;
}) {
  const hint = thousandsHint(value);
  return (
    <div className={className}>
      <MoneyInput value={value} onChange={onChange} disabled={disabled} className="w-full" />
      {/* Reserves its line whether or not there is a hint, so a row does not jolt taller the
          moment the first digit is typed. */}
      <span className="mt-0.5 block h-3 text-right text-[10px] leading-3 tabular-nums text-slate-600">
        {hint ?? ""}
      </span>
    </div>
  );
}
