"use client";

/**
 * The pieces the three AIP Entry panels share (PPDO-89).
 *
 * ⚠️ **Kept here rather than duplicated per panel.** The child list is the one concession the
 * drill-down makes to overview (spec decision 3): names and totals, never fields. Three copies of
 * it would drift, and a project row and an activity row disagreeing about where the total sits is
 * exactly the confusion the tree was replaced to remove.
 */

import { useMemo, useState } from "react";
import { fmtThousands } from "@/lib/aip-units";
import { AipFigureStrip, AipUnitCaption, type AipRowAmounts } from "./AipRowFigures";
import { aipRefSegment } from "./AipEntrySelection";

// ── The office band above the picker ────────────────────────────────────────

/**
 * FY · office · the office's figures, sticky at the top of the work area (decision 4).
 *
 * ⚠️ Replaces each group's block header, and is sticky because the drill-down took the office row
 * off the screen. Submit is an office-level action and the ceiling is an office-level number; a
 * figure the encoder has to scroll up past three panels to check is a figure they stop checking.
 *
 * ⚠️ **The office total is always on, the per-group breakdown is behind a disclosure.** The form
 * prints a subtotal per sub-office group, so the split has to be reachable — but the Office of the
 * Provincial Governor has **eight** groups, and eight strips pinned to the top of the viewport left
 * barely a panel's worth of screen underneath. Found by live-testing on PGO; a header that eats the
 * work area is not a context header. The ceiling the checklist gates on is office-wide, so the
 * office total is the figure that belongs in the always-visible line.
 */
export function AipOfficeHeader({
  fiscalYear, officeName, groups, action,
}: {
  fiscalYear: number;
  officeName: string;
  groups: { id: number; name: string; amounts: AipRowAmounts }[];
  /**
   * An office-level action, beside the group disclosure (PPDO-89). "+ Add programs" lives here
   * rather than in the page flow: it is the LDIP recovery path, not part of encoding, and under
   * the picker it read as a fourth step in a surface whose whole job is narrowing down.
   */
  action?: React.ReactNode;
}) {
  const several = groups.length > 1;
  const [open, setOpen] = useState(false);

  // ⚠️ Null-preserving, like every other AIP sum: a column no group carries stays blank rather
  // than printing 0.00, which would read as "measured, and it is zero" (`AipRowFigures`).
  const officeTotal = useMemo(
    () => sumAmounts(groups.map((g) => g.amounts)),
    [groups]
  );

  return (
    <div className="sticky top-0 z-20 border border-slate-200 bg-white px-4 py-3 shadow-sm">
      <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
        <div>
          <p className="text-sm font-semibold text-slate-800">
            FY {fiscalYear} · {officeName}
          </p>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <AipUnitCaption />
            {several && (
              <button
                type="button"
                onClick={() => setOpen((v) => !v)}
                aria-expanded={open}
                className="text-xs font-medium text-green-700 hover:underline"
              >
                {groups.length} sub-office groups {open ? "▾" : "▸"}
              </button>
            )}
            {action}
          </div>
        </div>
        <AipFigureStrip amounts={officeTotal} emphasis="strong" />
      </div>

      {several && open && (
        <div className="mt-2 space-y-1 border-t border-slate-100 pt-2">
          {groups.map((g) => (
            <div key={g.id} className="flex flex-wrap items-end justify-between gap-x-4 gap-y-1">
              <span className="text-[10px] font-semibold uppercase tracking-wide text-slate-600">
                {g.name}
              </span>
              <AipFigureStrip amounts={g.amounts} />
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

/** The office total across its groups — the same null-preserving add `sumActivityAmounts` uses. */
function sumAmounts(rows: readonly AipRowAmounts[]): AipRowAmounts {
  const add = (a: number | null, b: number | null): number | null =>
    a == null && b == null ? null : (a ?? 0) + (b ?? 0);
  return rows.reduce<AipRowAmounts>(
    (acc, r) => ({
      ps: add(acc.ps, r.ps),
      mooe: add(acc.mooe, r.mooe),
      co: add(acc.co, r.co),
      total: add(acc.total, r.total),
      ccAdaptation: add(acc.ccAdaptation, r.ccAdaptation),
      ccMitigation: add(acc.ccMitigation, r.ccMitigation),
    }),
    { ps: null, mooe: null, co: null, total: null, ccAdaptation: null, ccMitigation: null }
  );
}

// ── Child lists ─────────────────────────────────────────────────────────────

/**
 * An unresolved-comment count, as a badge.
 *
 * Renders nothing at zero: a permanent "0" on every row of every list is the noise the filter bar
 * already refuses to be. `rounded-full` is the badge exemption in CLAUDE.md, not a stray radius.
 */
export function AipUnresolvedBadge({ count }: { count: number }) {
  if (count <= 0) return null;
  return (
    <span
      className="shrink-0 rounded-full bg-amber-100 px-2 py-0.5 text-[11px] font-medium text-amber-900"
      title={`${count} unresolved comment${count === 1 ? "" : "s"}`}
    >
      💬 {count}
    </span>
  );
}

/**
 * One row of a panel's child list — the whole row selects, so the target is the width of the
 * panel rather than a link buried in it.
 */
export function AipChildRow({
  refCode, name, total, unresolved, onSelect,
}: {
  refCode: string;
  name: string;
  /** Null renders as an em dash: never costed, not costed at zero (V18-34). */
  total: number | null;
  unresolved: number;
  onSelect: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onSelect}
      className="flex w-full flex-wrap items-center gap-x-3 gap-y-1 border-b border-slate-100 px-3 py-2 text-left last:border-b-0 hover:bg-green-50"
    >
      <span className="w-10 shrink-0 font-mono text-xs font-semibold text-slate-800">
        {aipRefSegment(refCode)}
      </span>
      {/* whitespace-pre-line: activity names carry the encoder's own line breaks (PPDO-85). */}
      <span className="min-w-0 flex-1 whitespace-pre-line text-sm text-slate-800">{name}</span>
      <AipUnresolvedBadge count={unresolved} />
      <span className="shrink-0 tabular-nums text-sm text-slate-800">{fmtThousands(total)}</span>
    </button>
  );
}

/** The list's frame, so its empty state and its add control cannot be laid out two ways. */
export function AipChildList({
  title, count, emptyText, children, footer,
}: {
  title: string;
  count: number;
  emptyText: string;
  children: React.ReactNode;
  footer?: React.ReactNode;
}) {
  return (
    <section className="border border-slate-200 bg-white">
      <div className="flex items-center justify-between border-b border-slate-200 bg-slate-50 px-3 py-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-800">{title}</h3>
        <span className="text-xs text-slate-600">{count}</span>
      </div>
      {count === 0 ? (
        <p className="px-3 py-4 text-sm text-slate-600">{emptyText}</p>
      ) : (
        <div>{children}</div>
      )}
      {footer && <div className="border-t border-slate-200 px-3 py-2">{footer}</div>}
    </section>
  );
}

// ── Shared controls ─────────────────────────────────────────────────────────

/**
 * The inline name input behind a "+ Add …" link.
 *
 * ⚠️ Moved here from the entry page unchanged (PPDO-89). The created node is handed to the caller,
 * never discarded — discarding it is what forced the reload that read as the page refreshing.
 */
export function AipInlineAdd({
  label, placeholder, onAdd, disabled, disabledReason,
}: {
  label: string;
  placeholder: string;
  onAdd: (name: string) => Promise<void>;
  disabled?: boolean;
  /** ⚠️ Why it is disabled, never a bare greyed-out control — names who holds the work. */
  disabledReason?: string;
}) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);

  if (disabled) {
    return (
      <span className="text-xs text-slate-600" title={disabledReason}>
        {disabledReason ?? label}
      </span>
    );
  }

  if (!open) {
    return (
      <button type="button" onClick={() => setOpen(true)}
        className="text-xs font-medium text-green-700 hover:underline">{label}</button>
    );
  }
  return (
    <div className="flex gap-2">
      <input autoFocus value={name} onChange={(e) => setName(e.target.value)} placeholder={placeholder}
        className="flex-1 border border-slate-300 bg-white px-2 py-1 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600" />
      <button type="button" disabled={busy || !name.trim()}
        onClick={async () => {
          setBusy(true);
          try { await onAdd(name.trim()); setName(""); setOpen(false); } finally { setBusy(false); }
        }}
        className="bg-green-700 px-3 py-1 text-sm text-white disabled:bg-slate-300">Add</button>
      <button type="button" onClick={() => setOpen(false)}
        className="px-2 py-1 text-sm text-slate-600 hover:underline">Cancel</button>
    </div>
  );
}

/**
 * An error raised by something inside a panel.
 *
 * ⚠️ Inline at the top of the panel it came from, not a toast (spec §6): the input and the
 * selection are kept, and the reader needs the message to still be there while they fix it.
 */
export function AipPanelError({ message, onReload }: { message: string; onReload?: () => void }) {
  return (
    <p role="alert" className="border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
      {message}
      {onReload && (
        <button type="button" onClick={onReload} className="ml-2 font-medium underline">
          Reload
        </button>
      )}
    </p>
  );
}

/** The panel's own frame — one border, one padding rhythm across the three levels. */
export function AipPanel({ children }: { children: React.ReactNode }) {
  return <div className="space-y-3 border border-slate-200 bg-white p-4">{children}</div>;
}
