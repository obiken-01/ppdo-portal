"use client";

/**
 * InfoTip — small circular "i" button that reveals a short explanatory popover.
 *
 * For the one- or two-sentence asides that would otherwise sit on the page as
 * permanent helper text — "why is this read-only", "where does this figure come
 * from". Opens on hover, and on click or keyboard focus, which is what keeps it
 * reachable on touch and by screen readers. Escape or a click outside closes it.
 *
 * For anything longer, or anything the user has to acknowledge, use MessageDialog.
 * The glyph follows MessageDialog's info marker rather than an icon set — the
 * portal draws its status markers as text glyphs, not SVGs.
 *
 * Usage:
 *   <InfoTip label="Why is there no division split here?">
 *     The ceiling is divided among the office&apos;s divisions by the PPDO
 *     finance officer.
 *   </InfoTip>
 */

import { useEffect, useId, useRef, useState } from "react";

export interface InfoTipProps {
  children: React.ReactNode;
  /** Accessible name for the button — phrase it as the question the tip answers. */
  label?: string;
  /** Which edge of the button the panel lines up with. Flip to "right" near the page edge. */
  align?: "left" | "right";
}

export default function InfoTip({
  children,
  label = "More information",
  align = "left",
}: InfoTipProps) {
  // Two independent reasons to be open. Kept apart so that moving the mouse away
  // does not dismiss a tip the user deliberately clicked open.
  const [hovered, setHovered] = useState(false);
  const [pinned, setPinned] = useState(false);
  const open = hovered || pinned;

  const wrapRef = useRef<HTMLSpanElement>(null);
  const panelId = useId();

  useEffect(() => {
    if (!open) return;

    function close() {
      setHovered(false);
      setPinned(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") close();
    }
    function onMouseDown(e: MouseEvent) {
      if (wrapRef.current && !wrapRef.current.contains(e.target as Node)) close();
    }

    document.addEventListener("keydown", onKeyDown);
    document.addEventListener("mousedown", onMouseDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
      document.removeEventListener("mousedown", onMouseDown);
    };
  }, [open]);

  return (
    <span
      ref={wrapRef}
      className="relative inline-flex"
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
    >
      <button
        type="button"
        aria-label={label}
        aria-expanded={open}
        aria-describedby={open ? panelId : undefined}
        onClick={() => setPinned((v) => !v)}
        onFocus={() => setHovered(true)}
        onBlur={() => setHovered(false)}
        className="w-4 h-4 shrink-0 rounded-full bg-info-100 text-info-500 text-[10px] font-semibold leading-none flex items-center justify-center transition-colors hover:bg-info-500 hover:text-white focus:outline-none focus:ring-2 focus:ring-info-500 focus:ring-offset-1"
      >
        i
      </button>
      {open && (
        <span
          id={panelId}
          role="tooltip"
          className={`absolute top-6 z-20 w-64 border border-slate-200 bg-white p-3 text-xs font-normal leading-relaxed text-slate-600 shadow-md ${
            align === "right" ? "right-0" : "left-0"
          }`}
        >
          {children}
        </span>
      )}
    </span>
  );
}
