"use client";

import Link from "next/link";

/**
 * RowActions — standardized action-button cluster for table row actions.
 * Discussed and scoped 2026-08-11; replaces the four hand-rolled action
 * styles that had drifted across Items Master, PR List, Resource Links,
 * and LDIP (bare icons, bordered chips, and underlined text links all
 * doing the same job differently).
 *
 * Every button renders at the same fixed width (BTN_W), sized to comfortably
 * fit the longest real label in the app ("Finalize" / "Archive") — not
 * stretched, not shrunk to its own text. Two earlier versions got this
 * wrong: a CSS-grid version with w-full stretched short labels like "View"
 * into an oversized box; a flex-wrap version with natural per-button width
 * made every button a different size and wrapped unpredictably. Ralph
 * caught both live. Fixed width is what actually reads as one component
 * instead of several buttons that happen to sit near each other.
 *
 * Single row, right-aligned, no wrapping — see docs/DESIGN_SYSTEM.md §6a.
 * LDIP's 3 is the current max anywhere in the portal; the table's own
 * horizontal scroll absorbs it same as any other wide row. 5+ actions want
 * a primary action + overflow menu instead of wrapping this onto a second
 * line — NOT YET BUILT. No page needs it today; build the menu when one
 * does.
 */

const BTN_W = 80; // px — fits "Finalize" / "Archive" without wrapping or clipping

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

export default function RowActions({ actions }: { actions: RowAction[] }) {
  if (actions.length === 0) return null;

  return (
    <div className="flex gap-1 justify-end">
      {actions.map((action) => (
        <ActionButton key={action.key} action={action} />
      ))}
    </div>
  );
}

function ActionButton({ action }: { action: RowAction }) {
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
      <Link href={action.href} className={cls} style={{ width: BTN_W }}>
        {content}
      </Link>
    );
  }

  return (
    <button
      onClick={action.onClick}
      disabled={action.disabled || action.loading}
      className={cls}
      style={{ width: BTN_W }}
    >
      {content}
    </button>
  );
}
