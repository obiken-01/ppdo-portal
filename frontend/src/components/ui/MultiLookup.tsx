"use client";

/**
 * MultiLookup — the multi-select sibling of `Lookup` (PPDO-100).
 *
 * Selected values render as removable chips above a type-to-filter input; the list narrows as you
 * type, Enter picks the highlighted option, Backspace on an empty input removes the last chip.
 *
 * ⚠️ **Values are STRINGS, not ids.** `Lookup` is `value: number | null` because it picks a row.
 * This picks a set of *codes* that a single text column stores joined — an AIP activity's
 * implementing offices are `PEO/PGSO` in one column — so the code is the value, and a caller that
 * wants ids can key its own items by them. This is also what makes `allowCustomValues` possible at
 * all: a typed value that matches no row still has a value.
 *
 * ⚠️ **`allowCustomValues` is built but off by default** (Ralph, 2026-09-16). He expects to need
 * "type SJDH, press Enter" later, and asked for the component to support it while the one call site
 * that exists today stays strict. So the behaviour is here and tested by its own call, rather than
 * being retro-fitted into a component whose shape assumed a closed list.
 *
 * ⚠️ Like `Lookup`, this never fetches. The caller passes `items` already loaded.
 */

import { useEffect, useMemo, useRef, useState } from "react";

export interface MultiLookupProps<T> {
  items: T[];
  /** The selected codes, in the order they should render and save. */
  value: string[];
  onChange: (value: string[]) => void;
  /** The stored value for an item — what lands in `value`. */
  getValue: (item: T) => string;
  /** Display text for the option row. */
  getLabel: (item: T) => string;
  /** What typed text matches against. Defaults to `getLabel`. */
  getSearchText?: (item: T) => string;
  /**
   * Accept a typed value that matches no item, on Enter. Off by default — see the header. A caller
   * turning this on is saying the list is a convenience, not a constraint.
   */
  allowCustomValues?: boolean;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  /** Rendered under the field, e.g. to explain what the saved value will look like. */
  hint?: React.ReactNode;
}

export default function MultiLookup<T>({
  items, value, onChange, getValue, getLabel, getSearchText,
  allowCustomValues = false, placeholder, disabled, className, hint,
}: MultiLookupProps<T>) {
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(0);
  const boxRef = useRef<HTMLDivElement>(null);

  // Closes on an outside click, like `Lookup` — a combobox left open behind the next field is the
  // thing that made the hand-rolled ones feel broken.
  useEffect(() => {
    if (!open) return;
    function onDocClick(e: MouseEvent) {
      if (boxRef.current && !boxRef.current.contains(e.target as Node)) setOpen(false);
    }
    document.addEventListener("mousedown", onDocClick);
    return () => document.removeEventListener("mousedown", onDocClick);
  }, [open]);

  // ⚠️ Already-picked items are filtered OUT, not just marked. Picking the same office twice would
  // store `PEO/PEO`, which the form would print verbatim.
  const options = useMemo(() => {
    const picked = new Set(value);
    const q = query.trim().toLowerCase();
    return items
      .filter((i) => !picked.has(getValue(i)))
      .filter((i) => q === "" || (getSearchText?.(i) ?? getLabel(i)).toLowerCase().includes(q));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [items, value, query]);

  useEffect(() => { setActive(0); }, [query]);

  function add(next: string) {
    const trimmed = next.trim();
    if (!trimmed || value.includes(trimmed)) return;
    onChange([...value, trimmed]);
    setQuery("");
    setOpen(false);
  }

  function removeAt(index: number) {
    onChange(value.filter((_, i) => i !== index));
  }

  function onKeyDown(e: React.KeyboardEvent<HTMLInputElement>) {
    if (e.key === "ArrowDown") {
      e.preventDefault();
      setOpen(true);
      setActive((a) => Math.min(a + 1, options.length - 1));
    } else if (e.key === "ArrowUp") {
      e.preventDefault();
      setActive((a) => Math.max(a - 1, 0));
    } else if (e.key === "Enter") {
      // ⚠️ Always prevented: this sits inside a form, and an un-prevented Enter submits it while
      // the reader thinks they are picking an option.
      e.preventDefault();
      const option = options[active];
      if (option) add(getValue(option));
      else if (allowCustomValues) add(query);
    } else if (e.key === "Backspace" && query === "" && value.length > 0) {
      removeAt(value.length - 1);
    } else if (e.key === "Escape") {
      setOpen(false);
    }
  }

  return (
    <div ref={boxRef} className={`relative ${className ?? ""}`}>
      <div
        className={`flex flex-wrap items-center gap-1 border border-slate-300 bg-white px-1.5 py-1 ${
          disabled ? "bg-slate-100" : "focus-within:ring-1 focus-within:ring-green-600"
        }`}
      >
        {value.map((v, i) => (
          // `rounded-full` on a chip is allowed — CLAUDE.md flattens cards and inputs, not pills.
          <span key={`${v}-${i}`}
            className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-800">
            {v}
            {!disabled && (
              <button type="button" onClick={() => removeAt(i)} aria-label={`Remove ${v}`}
                className="text-slate-600 hover:text-red-700">
                ×
              </button>
            )}
          </span>
        ))}
        <input
          value={query}
          disabled={disabled}
          placeholder={value.length === 0 ? placeholder : ""}
          onChange={(e) => { setQuery(e.target.value); setOpen(true); }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          className="min-w-24 flex-1 bg-transparent px-0.5 py-0.5 text-xs text-slate-800 focus:outline-none disabled:bg-transparent"
        />
      </div>

      {hint && <p className="mt-0.5 text-[11px] text-slate-600">{hint}</p>}

      {open && !disabled && (
        <ul className="absolute z-30 mt-0.5 max-h-56 w-full overflow-y-auto border border-slate-200 bg-white shadow-lg">
          {options.length === 0 ? (
            <li className="px-2 py-1.5 text-xs text-slate-600">
              {/* Says which of the two situations this is, because they need different actions. */}
              {allowCustomValues && query.trim()
                ? `Press Enter to add “${query.trim()}”`
                : query.trim() ? "No match." : "Nothing left to add."}
            </li>
          ) : (
            options.map((item, i) => (
              <li key={getValue(item)}>
                <button
                  type="button"
                  onMouseEnter={() => setActive(i)}
                  onClick={() => add(getValue(item))}
                  className={`block w-full px-2 py-1.5 text-left text-xs ${
                    i === active ? "bg-green-50 text-slate-800" : "text-slate-800 hover:bg-slate-50"
                  }`}
                >
                  {getLabel(item)}
                </button>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  );
}

/**
 * The AIP form writes several implementing offices into ONE text column, separated by `/` — the
 * separator the province's own file uses (`5% CF/ NGA`) and the one the export prints. So what is
 * stored is exactly what is printed, and no migration was needed (Ralph, 2026-09-16).
 *
 * ⚠️ Segments are kept even when they match no configured office. An FY≤2027 activity was imported
 * with whatever the file said, and dropping a value the encoder can see would lose data on a save
 * they did not think was destructive.
 */
export function splitCodes(stored: string | null | undefined): string[] {
  return (stored ?? "")
    .split("/")
    .map((s) => s.trim())
    .filter((s) => s.length > 0);
}

/** The inverse of `splitCodes` — `null` when nothing is picked, so the column clears. */
export function joinCodes(codes: string[]): string | null {
  const joined = codes.map((c) => c.trim()).filter(Boolean).join("/");
  return joined.length > 0 ? joined : null;
}

/**
 * The stored codes minus the proponent office, for editing (Ralph, 2026-09-16).
 *
 * ⚠️ The proponent's own office is always part of the saved value and always prints first, but is
 * never a chip — it is not a choice. Stripping it on load is what stops it appearing twice after a
 * save, and what stops an encoder being offered an × for something the next save restores.
 *
 * Case-insensitive, because the stored value on an imported row is whatever its file said.
 */
export function withoutProponent(stored: string | null | undefined, proponent: string | null): string[] {
  const codes = splitCodes(stored);
  if (!proponent) return codes;
  const own = proponent.trim().toLowerCase();
  return codes.filter((c) => c.toLowerCase() !== own);
}

/** The picked codes with the proponent office back at the FRONT, ready to store. */
export function withProponent(codes: string[], proponent: string | null): string[] {
  const own = proponent?.trim();
  if (!own) return codes;
  return [own, ...codes.filter((c) => c.toLowerCase() !== own.toLowerCase())];
}
