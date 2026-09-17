"use client";

/**
 * ClampedText — long free text cut at N lines, with a "more / less" toggle (PPDO-105).
 *
 * ↩️ **Extracted from the WFP page's local `ClampedName`**, which already did this for program,
 * project and activity names. It is the portal's one rule for long text in a list table
 * (`docs/DESIGN_SYSTEM.md` §5): clamp the text, never the identifier beside it.
 *
 * ⚠️ **"more" only appears when the text is actually cut.** It is measured, not guessed from a
 * character count — a 60-character name in a narrow column clamps, a 200-character one in a wide
 * column may not, and a toggle that expands nothing reads as broken.
 *
 * ⚠️ **Re-measured on resize.** Column widths change with the window, and a name that fit at one
 * width is clamped at another. The WFP original measured once, so narrowing the window left the text
 * cut with no way to expand it.
 *
 * ⚠️ **Never use this on the Annex B grid.** That grid previews the printed form, where every line of
 * a name prints; clamping it would hide the row heights PPDO-85 warns about.
 */

import { useEffect, useLayoutEffect, useRef, useState } from "react";

// ⚠️ Spelled out, not built as `line-clamp-${lines}`: Tailwind only ships classes it can find written
// whole in the source, so a template string would compile to nothing and clamp nothing.
const CLAMP_CLASS = {
  1: "line-clamp-1",
  2: "line-clamp-2",
  3: "line-clamp-3",
} as const;

export default function ClampedText({
  text,
  lines = 2,
  preserveLineBreaks = true,
  className = "",
}: {
  text: string;
  /** How many lines to show before "more". Two is the portal default. */
  lines?: 1 | 2 | 3;
  /**
   * Keep the author's own line breaks (`whitespace-pre-line`). On by default because AIP activity
   * names carry the encoder's line breaks on purpose (PPDO-85), and collapsing them turns a
   * structured name into one run-on line.
   */
  preserveLineBreaks?: boolean;
  className?: string;
}) {
  const [expanded, setExpanded] = useState(false);
  const [isClamped, setIsClamped] = useState(false);
  const ref = useRef<HTMLSpanElement>(null);

  // Measured before paint, so a clamped name never flashes without its "more".
  useLayoutEffect(() => {
    if (expanded) return;
    const el = ref.current;
    if (el) setIsClamped(el.scrollHeight > el.clientHeight + 1);
  }, [text, expanded, lines]);

  useEffect(() => {
    const el = ref.current;
    if (!el || expanded || typeof ResizeObserver === "undefined") return;
    const observer = new ResizeObserver(() => {
      setIsClamped(el.scrollHeight > el.clientHeight + 1);
    });
    observer.observe(el);
    return () => observer.disconnect();
  }, [expanded]);

  return (
    <span className={className}>
      <span
        ref={ref}
        className={`${expanded ? "" : CLAMP_CLASS[lines]} ${preserveLineBreaks ? "whitespace-pre-line" : ""}`}
        // The full text on hover — the quickest way to read it without changing the row's height.
        title={!expanded && isClamped ? text : undefined}
      >
        {text}
      </span>
      {(isClamped || expanded) && (
        <button
          type="button"
          // ⚠️ Stopped, because the row around this may itself be clickable. Expanding a name must
          // not also open the record.
          onClick={(e) => { e.stopPropagation(); setExpanded((p) => !p); }}
          aria-expanded={expanded}
          className="ml-1 whitespace-nowrap text-xs text-green-700 hover:underline"
        >
          {expanded ? "less" : "more"}
        </button>
      )}
    </span>
  );
}
