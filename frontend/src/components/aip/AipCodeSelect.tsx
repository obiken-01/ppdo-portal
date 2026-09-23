"use client";

import type { AipCodeOption } from "@/hooks/useAipCodeOptions";

/**
 * A config-backed code picker for the AIP editors — eSRE and CC typology
 * (Demo 2.2 / PPDO-124, Demo 2.3 / PPDO-125).
 *
 * ⚠️ <b>The one rule that matters: whatever is already stored stays selectable.</b> Two different
 * failures collapse into that single guarantee, which is why it is a rule and not a special case:
 *
 * <ul>
 *   <li>a code was <b>deactivated</b> in config after an activity was saved against it, and</li>
 *   <li>the config fetch <b>failed</b>, so every code looks unknown.</li>
 * </ul>
 *
 * In both, a picker built only from the active list would render with no matching option. The
 * browser then shows the first option — here the blank one — and the next save writes that blank
 * back. The encoder never touched the field, and the value is gone. Keeping the stored value as
 * an option makes the field honest about what it holds in both cases, and the `(inactive)` marker
 * says which one the user is looking at.
 */
export default function AipCodeSelect({
  value,
  onChange,
  options,
  loaded,
  className,
  ariaLabel,
}: {
  value: string;
  onChange: (next: string) => void;
  options: AipCodeOption[];
  /** From `useAipCodeOptions`. Suppresses the "(inactive)" marker until the list has settled. */
  loaded: boolean;
  className?: string;
  ariaLabel?: string;
}) {
  const current = value.trim();
  const known = options.some((o) => o.code === current);

  // ⚠️ Only while `loaded` — before the fetch settles NOTHING is known, and labelling a perfectly
  // current code "(inactive)" for a moment on every page load would teach encoders to ignore the
  // marker on the one occasion it is true.
  const orphaned = current !== "" && loaded && !known;

  /**
   * ⚠️ <b>Code AND name, everywhere</b> — agreed with the finance officer (PPDO-125): the list
   * shows both, and the <b>code</b> is what is stored on the activity. It is tempting to show the
   * bare code in the two narrow table-row editors, where a JMC typology name runs to a full
   * sentence and the closed control will truncate it. Resist that: a bare `M511-01` is
   * unreadable to the person choosing, and truncation still leaves the code and the first words
   * visible, which is enough to tell two options apart. The full text is on the option's title.
   */
  function label(o: AipCodeOption): string {
    // `name` is seeded equal to `code` across every CC typology today, so guard against rendering
    // "A222-01 — A222-01". Collapses automatically once the JMC 2013-01 names are seeded.
    if (o.name === o.code) return o.code;
    return `${o.code} — ${o.name}`;
  }

  return (
    <select
      value={current}
      onChange={(e) => onChange(e.target.value)}
      aria-label={ariaLabel}
      className={className}
    >
      <option value="">—</option>
      {orphaned && <option value={current}>{current} (inactive)</option>}
      {options.map((o) => (
        <option key={o.code} value={o.code} title={o.description ?? undefined}>
          {label(o)}
        </option>
      ))}
    </select>
  );
}
