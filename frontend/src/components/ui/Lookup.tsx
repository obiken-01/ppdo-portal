"use client";

/**
 * Lookup — generic shared searchable-combobox picker (RAL-60 follow-up).
 *
 * Generalizes the type-to-filter pattern (originally AccountCombobox in the WFP
 * expenditure popup, then OfficeSelect) into a reusable primitive for any lookup
 * list — offices, accounts, divisions, funding sources, ... — instead of each
 * page hand-rolling its own combobox or a plain <select>.
 *
 * Caller supplies the already-fetched `items` list plus small id/label/search
 * accessors; this component only searches/renders, it never fetches. Presets
 * for a specific entity (e.g. OfficeSelect) should wrap this with their own
 * sort order and accessors baked in, so call sites stay simple.
 *
 * Note: `getId`/`getLabel`/`getSearchText`/`renderOption` are read on every
 * render but deliberately excluded from the internal effect/memo dependency
 * arrays (matching the original AccountCombobox/OfficeSelect behavior) — most
 * callers pass fresh inline arrow functions each render, and including them
 * would defeat memoization without preventing any bug. Keep them pure functions
 * of the item (no closures over frequently-changing external state) so a stale
 * read between one `items`/`value` change and the next is never observable.
 */

import { useEffect, useMemo, useRef, useState } from "react";

export interface LookupProps<T> {
  items: T[];
  value: number | null;
  onChange: (id: number | null) => void;
  getId: (item: T) => number;
  /** Display text — used both in the closed input and as the default row content. */
  getLabel: (item: T) => string;
  /** What typed text matches against. Defaults to getLabel. */
  getSearchText?: (item: T) => string;
  /** Custom row content. Defaults to plain getLabel() text. */
  renderOption?: (item: T) => React.ReactNode;
  /** Label for the "no selection" row, e.g. "All Offices". Omit to require a pick. */
  allOptionLabel?: string;
  placeholder?: string;
  disabled?: boolean;
  className?: string;
  maxResults?: number;
}

export default function Lookup<T>({
  items,
  value,
  onChange,
  getId,
  getLabel,
  getSearchText,
  renderOption,
  allOptionLabel,
  placeholder = "Search…",
  disabled,
  className,
  maxResults = 30,
}: LookupProps<T>) {
  const searchText = getSearchText ?? getLabel;

  const [query, setQuery] = useState("");
  // The box always shows *something* (the current selection's label, or
  // allOptionLabel when nothing is selected) even before the user has typed —
  // otherwise it'd sit blank. `isSearching` distinguishes "this text is just
  // the pre-filled display value" from "the user actually typed this to
  // filter" — without it, reopening a populated field (or opening one that
  // defaults to allOptionLabel, e.g. a dashboard's "All Offices") would filter
  // the real list against that leftover display text and wrongly show zero
  // matches, since no item's search text contains its own full label/the
  // all-option wording.
  const [isSearching, setIsSearching] = useState(false);
  const [open, setOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  // Keep the display text in sync with the selected id — including on first
  // render/list load and whenever `value` changes externally (e.g. a ?id= URL
  // prefill or a "locked to own X" effect).
  useEffect(() => {
    const found = value != null ? items.find((i) => getId(i) === value) ?? null : null;
    setQuery(found ? getLabel(found) : allOptionLabel ?? "");
    setIsSearching(false);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, items]);

  useEffect(() => {
    function onMouseDown(e: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(e.target as Node)) {
        setOpen(false);
        const found = value != null ? items.find((i) => getId(i) === value) ?? null : null;
        setQuery(found ? getLabel(found) : allOptionLabel ?? "");
        setIsSearching(false);
      }
    }
    document.addEventListener("mousedown", onMouseDown);
    return () => document.removeEventListener("mousedown", onMouseDown);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [value, items]);

  const filtered = useMemo(() => {
    const q = isSearching ? query.trim().toLowerCase() : "";
    const matches = !q ? items : items.filter((i) => searchText(i).toLowerCase().includes(q));
    return matches.slice(0, maxResults);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [query, items, maxResults, isSearching]);

  const showAllOption =
    allOptionLabel != null &&
    (!isSearching || allOptionLabel.toLowerCase().includes(query.trim().toLowerCase()));

  return (
    <div ref={containerRef} className={`relative ${className ?? ""}`}>
      <input
        type="text"
        value={query}
        disabled={disabled}
        onFocus={() => setOpen(true)}
        onChange={(e) => {
          setQuery(e.target.value);
          setIsSearching(true);
          setOpen(true);
        }}
        placeholder={placeholder}
        className="w-full border border-slate-200 bg-white text-sm px-3 py-1.5 text-slate-700 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:opacity-60 disabled:cursor-not-allowed"
      />
      {open && !disabled && (showAllOption || filtered.length > 0) && (
        <div className="absolute z-50 top-full left-0 w-full min-w-[16rem] bg-white border border-slate-200 shadow-lg max-h-64 overflow-y-auto">
          {showAllOption && (
            <button
              type="button"
              onMouseDown={(e) => e.preventDefault()}
              onClick={() => {
                onChange(null);
                setQuery(allOptionLabel ?? "");
                setOpen(false);
              }}
              className="w-full text-left px-3 py-1.5 text-sm text-slate-600 italic hover:bg-green-50 hover:text-green-800 border-b border-slate-100"
            >
              {allOptionLabel}
            </button>
          )}
          {filtered.map((item) => {
            const id = getId(item);
            return (
              <button
                key={id}
                type="button"
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => {
                  onChange(id);
                  setQuery(getLabel(item));
                  setOpen(false);
                }}
                className="w-full text-left px-3 py-1.5 text-sm hover:bg-green-50 hover:text-green-800 border-b border-slate-50 last:border-0"
              >
                {renderOption ? renderOption(item) : getLabel(item)}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
