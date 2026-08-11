"use client";

import Link from "next/link";

/**
 * RowActions — standardized action-button cluster for table row actions.
 * Discussed and scoped 2026-08-11; replaces the four hand-rolled action
 * styles that had drifted across Items Master, PR List, Resource Links,
 * and LDIP (bare icons, bordered chips, and underlined text links all
 * doing the same job differently).
 *
 * Buttons keep their own natural width — never stretched to fill a shared
 * column. A CSS-grid version tried that first and made short labels like
 * "View" render as an oversized box; not repeating that mistake.
 *
 * Wrapping — see docs/DESIGN_SYSTEM.md §6a:
 *   Buttons flex-wrap right-aligned inside a max-width. 1-2 actions sit on
 *   one line; a 3rd wraps to its own line below rather than forcing an
 *   even 2x2 split. 5+ actions want a primary action + overflow menu —
 *   NOT YET BUILT. No page needs it today (LDIP's 3 is the current max
 *   anywhere) — build the menu when a page actually grows into it.
 */

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
  "inline-flex items-center justify-center gap-1 px-2.5 py-1 text-xs font-medium border transition-colors disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap";

export default function RowActions({ actions }: { actions: RowAction[] }) {
  if (actions.length === 0) return null;

  return (
    <div className="flex flex-wrap gap-1 justify-end max-w-[180px] ml-auto">
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
      <Link href={action.href} className={cls}>
        {content}
      </Link>
    );
  }

  return (
    <button
      onClick={action.onClick}
      disabled={action.disabled || action.loading}
      className={cls}
    >
      {content}
    </button>
  );
}
