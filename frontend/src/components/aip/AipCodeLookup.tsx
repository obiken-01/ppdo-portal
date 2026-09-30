"use client";

import { useMemo } from "react";
import Lookup from "@/components/ui/Lookup";
import type { AipCodeOption } from "@/hooks/useAipCodeOptions";

/**
 * The searchable twin of `AipCodeSelect`, for code lists too long to scroll (v1.8.0 — PPDO-172).
 *
 * CC typology is the case: 282 active codes since the LGU list was seeded (PPDO-139), in a plain
 * `<select>` that could only be scrolled. This is the shared `Lookup`, so the encoder types a code
 * ("A113") or a word from its name ("awareness") and picks from the matches. eSRE, at four codes,
 * stays a select.
 *
 * ⚠️ <b>Same rule as `AipCodeSelect`: whatever is already stored stays selectable.</b> A code that
 * was deactivated after being saved, or every code when the config fetch failed, is kept as an
 * "(inactive)" item — otherwise the box would show nothing and the next save would write the blank
 * back without the encoder touching the field. See that component for the full reasoning.
 */
interface Item {
  id: number;
  code: string;
  label: string;
  search: string;
}

export default function AipCodeLookup({
  value,
  onChange,
  options,
  loaded,
  className,
  placeholder = "Search code or name…",
}: {
  value: string;
  onChange: (next: string) => void;
  options: AipCodeOption[];
  /** From `useAipCodeOptions`. Suppresses the "(inactive)" marker until the list has settled. */
  loaded: boolean;
  className?: string;
  placeholder?: string;
}) {
  const current = value.trim();

  // `Lookup` keys items by number, and codes are strings — so each gets its position as its id.
  const items = useMemo<Item[]>(() => {
    const list: Item[] = options.map((o, i) => ({
      id: i,
      code: o.code,
      // Code AND name, as in AipCodeSelect — a bare code is unreadable to the person choosing.
      label: o.name === o.code ? o.code : `${o.code} — ${o.name}`,
      search: `${o.code} ${o.name} ${o.description ?? ""}`,
    }));
    const known = options.some((o) => o.code === current);
    // ⚠️ Only once `loaded`: before the fetch settles nothing is known, and marking a current code
    // "(inactive)" on every page load would teach encoders to ignore the one true warning.
    if (current !== "" && loaded && !known) {
      list.push({ id: list.length, code: current, label: `${current} (inactive)`, search: current });
    } else if (current !== "" && !loaded) {
      // Still loading: keep the saved code showing rather than an empty box.
      list.push({ id: list.length, code: current, label: current, search: current });
    }
    return list;
  }, [options, current, loaded]);

  const selected = current === "" ? null : items.find((i) => i.code === current)?.id ?? null;

  return (
    <Lookup<Item>
      items={items}
      value={selected}
      onChange={(id) => onChange(id == null ? "" : items.find((i) => i.id === id)?.code ?? "")}
      getId={(i) => i.id}
      getLabel={(i) => i.label}
      getSearchText={(i) => i.search}
      allOptionLabel="—"
      placeholder={placeholder}
      className={className}
    />
  );
}
