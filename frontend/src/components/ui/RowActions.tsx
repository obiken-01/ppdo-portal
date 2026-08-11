"use client";

import Link from "next/link";

/**
 * RowActions — standardized action-button cluster for table row actions.
 * Discussed and scoped 2026-08-11; replaces the four hand-rolled action
 * styles that had drifted across Items Master, PR List, Resource Links,
 * and LDIP (bare icons, bordered chips, and underlined text links all
 * doing the same job differently).
 *
 * Layout rule — see docs/DESIGN_SYSTEM.md:
 *   1 action    -> single button, spans the fixed-width column
 *   2 actions   -> two across, one row
 *   3-4 actions -> two across, wrapping; an odd trailing action spans both columns
 *   5+ actions  -> primary action + overflow menu — NOT YET BUILT. No page
 *                  needs it today (LDIP's 3 is the current max anywhere).
 *                  Build the overflow menu when a real page needs it —
 *                  don't grow this component's grid past 4 to cover it.
 *
 * Every row renders inside the same fixed-width container regardless of
 * action count, so the Actions column doesn't change width as you scan
 * down a table with mixed row states.
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
  "w-full inline-flex items-center justify-center gap-1 px-2 py-1.5 text-xs font-medium border transition-colors disabled:opacity-50 disabled:cursor-not-allowed whitespace-nowrap";

export default function RowActions({ actions }: { actions: RowAction[] }) {
  if (actions.length === 0) return null;

  const isOdd = actions.length % 2 === 1;

  return (
    <div className="grid grid-cols-2 gap-1 w-[170px] ml-auto">
      {actions.map((action, i) => {
        const isLast = i === actions.length - 1;
        const spanFull = actions.length === 1 || (isOdd && isLast);
        return (
          <div key={action.key} className={spanFull ? "col-span-2" : undefined}>
            <ActionButton action={action} />
          </div>
        );
      })}
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
