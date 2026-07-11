"use client";

/**
 * MoneyInput — shared peso-prefixed text input for monetary amounts (v1.2, RAL-100).
 *
 * Usage:
 *   <MoneyInput
 *     value={amount}          // number | null
 *     onChange={setAmount}    // (v: number | null) => void — always receives a clean number
 *     placeholder="0.00"
 *     disabled={readonly}
 *     className="w-36"        // controls width; also accepts text-xs etc.
 *   />
 *
 * Behaviour:
 *   - Displays a ₱ prefix label (non-editable) + a text input for the amount.
 *   - type="text" + inputMode="decimal" — allows comma display (type="number" cannot).
 *   - Holds a number internally; live comma formatting while typing with caret-position
 *     preservation (counts significant chars left of caret, re-maps after reformat).
 *   - Accepts digits + one decimal point; strips all other characters; caps at 2dp.
 *   - onChange emits the CLEAN numeric value (commas are display-only, never emitted).
 *   - onChange emits null when the field is empty or contains only a bare decimal point.
 *   - Normalises to full 2dp on blur (e.g. "1.5" → "1.50").
 *   - Flat design — no rounded corners, green focus ring, matches existing form inputs.
 */

import { useRef, useState } from "react";
import { formatMoney, parseMoney } from "@/lib/money";

export interface MoneyInputProps {
  value: number | null;
  onChange: (value: number | null) => void;
  disabled?: boolean;
  placeholder?: string;
  /** Extra classes on the outer wrapper — use for width and text-size overrides. */
  className?: string;
  min?: number;
}

export default function MoneyInput({
  value,
  onChange,
  disabled,
  placeholder = "0.00",
  className,
}: MoneyInputProps) {
  const inputRef = useRef<HTMLInputElement>(null);

  // While focused: local display string (with live commas).
  // While blurred: derived from the value prop so external updates are reflected.
  const [focused, setFocused] = useState(false);
  const [localDisplay, setLocalDisplay] = useState("");

  const displayValue = focused
    ? localDisplay
    : value != null
    ? formatMoney(value)
    : "";

  function handleFocus() {
    setLocalDisplay(value != null ? formatMoney(value) : "");
    setFocused(true);
  }

  function handleBlur() {
    setFocused(false);
    const parsed = parseMoney(localDisplay);
    // Normalise display to full 2dp (e.g. user left "1." or "1.5")
    setLocalDisplay(parsed != null ? formatMoney(parsed) : "");
    onChange(parsed);
  }

  function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const input = e.currentTarget;
    const raw = input.value;
    const caretBefore = input.selectionStart ?? raw.length;

    // Count significant chars (digits + decimal point) left of caret — used to
    // reposition the caret after the formatted string replaces the raw one.
    let sigLeft = 0;
    for (let i = 0; i < caretBefore; i++) {
      if (/[\d.]/.test(raw[i])) sigLeft++;
    }

    // 1. Strip commas and anything that isn't a digit or decimal point.
    let stripped = raw.replace(/[^\d.]/g, "");
    // 2. Keep only the first decimal point.
    const dotIdx = stripped.indexOf(".");
    if (dotIdx !== -1) {
      stripped =
        stripped.slice(0, dotIdx + 1) +
        stripped.slice(dotIdx + 1).replace(/\./g, "");
    }
    // 3. Cap to 2 decimal places.
    if (dotIdx !== -1 && stripped.length > dotIdx + 3) {
      stripped = stripped.slice(0, dotIdx + 3);
    }

    // 4. Build the formatted display string, preserving a trailing "." while
    //    the user is still typing the decimal portion.
    let formatted: string;
    if (!stripped) {
      formatted = "";
    } else if (stripped === ".") {
      formatted = "0.";
    } else if (stripped.endsWith(".")) {
      const intNum = parseInt(stripped, 10) || 0;
      formatted = intNum.toLocaleString("en-PH") + ".";
    } else if (stripped.includes(".")) {
      const [intStr, decStr] = stripped.split(".");
      const intNum = parseInt(intStr || "0", 10);
      formatted = `${intNum.toLocaleString("en-PH")}.${decStr}`;
    } else {
      const intNum = parseInt(stripped, 10);
      formatted = isNaN(intNum) ? "" : intNum.toLocaleString("en-PH");
    }

    setLocalDisplay(formatted);

    // Emit the clean numeric value (null when empty/incomplete).
    const parsed =
      stripped && stripped !== "." ? parseMoney(stripped) : null;
    onChange(parsed);

    // Reposition caret after React re-renders the formatted value into the DOM.
    requestAnimationFrame(() => {
      if (!inputRef.current) return;
      const newVal = inputRef.current.value;
      let sigCount = 0;
      let newPos = newVal.length;
      for (let i = 0; i < newVal.length; i++) {
        if (/[\d.]/.test(newVal[i])) sigCount++;
        if (sigCount >= sigLeft) {
          newPos = i + 1;
          break;
        }
      }
      inputRef.current.setSelectionRange(newPos, newPos);
    });
  }

  return (
    <div
      className={`flex items-stretch border border-slate-200 bg-white focus-within:ring-1 focus-within:ring-green-500 ${
        disabled ? "opacity-60 cursor-not-allowed" : ""
      } ${className ?? ""}`}
    >
      <span className="shrink-0 border-r border-slate-200 bg-slate-50 flex items-center px-1.5 text-slate-600 select-none text-[0.65rem] leading-none">
        ₱
      </span>
      <input
        ref={inputRef}
        type="text"
        inputMode="decimal"
        value={displayValue}
        onChange={handleChange}
        onFocus={handleFocus}
        onBlur={handleBlur}
        disabled={disabled}
        placeholder={placeholder}
        className="flex-1 min-w-0 px-1.5 py-0.5 text-right tabular-nums text-slate-800 focus:outline-none bg-transparent disabled:cursor-not-allowed"
      />
    </div>
  );
}
