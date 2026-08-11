"use client";

import Link from "next/link";

/**
 * RowActions — standardized action-button cluster for table row actions.
 * Discussed and scoped 2026-08-11; replaces the four hand-rolled action
 * styles that had drifted across Items Master, PR List, Resource Links,
 * and LDIP (bare icons, bordered chips, and underlined text links all
 * doing the same job differently).
 *
 * Every button in one row renders at the SAME fixed width — not stretched,
 * not shrunk to its own text. Two earlier versions got this wrong: a
 * CSS-grid version with w-full stretched short labels like "View" into an
 * oversized box; a flex-wrap version with natural per-button width made
 * every button a different size and wrapped unpredictably. Ralph caught
 * both live. Fixed width is what actually reads as one component instead
 * of several buttons that happen to sit near each other.
 *
 * `btnWidth` defaults to 80px, which fits LDIP/AIP's longest labels
 * ("Finalize" / "Archive") without clipping. Pages with longer real labels
 * pass a wider value — e.g. PR List's "Mark Completed" (14 chars, the
 * longest in the app) needs ~112px. Don't widen the shared default to cover
 * one page's outlier label; every other page would carry unnecessary
 * padding for a word they never show.
 *
 * Wrapping — see docs/DESIGN_SYSTEM.md §6a:
 *   Exactly 2 fixed-width buttons fit per row; a 3rd wraps to its own row
 *   below at the SAME width as every other button, right-aligned — never
 *   stretched to fill the row alone. 5+ actions want a primary action +
 *   overflow menu — NOT YET BUILT. No page needs it today (LDIP's 3 is the
 *   current max anywhere) — build the menu when a page actually grows
 *   into it.
 */

const DEFAULT_BTN_W = 80; // px — fits "Finalize" / "Archive" without clipping
const GAP = 4;            // px — matches Tailwind's gap-1

export interface RowAction {
  key: string;
  label: string;
  /** Renders as a Link. Omit and use onClick for a button instead. */
  href?: string;
  onClick?: () => void;
  variant?: "default" | "primary" | "warn" | "danger";
  disabled?: boolean;
  loading?: boolean;
}

const VARIANT_CLS: Record<NonNullable<RowAction["variant"]>, string> = {
  default: "border-slate-300 text-slate-600 bg-white hover:bg-slate-50",
  primary: "border-green-300 text-green-700 bg-green-50 hover:bg-green-100",
  warn:    "border-amber-300 text-amber-700 bg-amber-50 hover:bg-amber-100",
  danger:  "border-danger-500/30 text-danger-500 bg-danger-100 hover:bg-danger-100/70",
};

const BTN_CLS =
  "inline-flex items-center justify-center gap-1 px-1.5 py-1 text-xs font-medium border transition-colors disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap shrink-0";

export default function RowActions({
  actions,
  btnWidth = DEFAULT_BTN_W,
}: {
  actions: RowAction[];
  /** Fixed width per button, in px. Only widen this for a page whose real
   * label is longer than "Finalize"/"Archive" — see the component doc comment. */
  btnWidth?: number;
}) {
  if (actions.length === 0) return null;

  return (
    <div
      className="flex flex-wrap gap-1 justify-end ml-auto"
      style={{ width: btnWidth * 2 + GAP }}
    >
      {actions.map((action) => (
        <ActionButton key={action.key} action={action} width={btnWidth} />
      ))}
    </div>
  );
}

function ActionButton({ action, width }: { action: RowAction; width: number }) {
  const variant = action.variant ?? "default";
  const cls = `${BTN_CLS} ${VARIANT_CLS[variant]}`;

  const content = (
    <>
      {action.loading && (
        <span className="w-3 h-3 border-2 border-current border-t-transparent rounded-full animate-spin" />
      )}
      {action.label}
    </>
  );

  if (action.href) {
    return (
      <Link href={action.href} className={cls} style={{ width }}>
        {content}
      </Link>
    );
  }

  return (
    <button
      onClick={action.onClick}
      disabled={action.disabled || action.loading}
      className={cls}
      style={{ width }}
    >
      {content}
    </button>
  );
}
