"use client";

/**
 * One section of the proposal editor (PPDO-160, spec §6.2 "Each section card", wireframe 3).
 *
 * Template heading, a status pill, the body, and a footer with **Discard** and **Save section**
 * (decision 24). H and M are filled from the AIP and have no footer actions. In the all-cards view a
 * card folds to a one-line summary; a folded card with unsaved edits keeps a **Save** in its header,
 * and folding never discards.
 *
 * ⚠️ **A stale save turns the footer amber** with the server's sentence and **Reload** / **Keep
 * editing** (spec §6.2 "Error (save)"). The input stays; nothing is thrown away until Reload is
 * confirmed.
 */

import type { ReactNode } from "react";
import type { SectionDef } from "@/lib/proposal-editor";

export type SectionStatus = "saved" | "unsaved" | "fromAip" | "notStarted" | "error";

const PILL: Record<SectionStatus, { cls: string; label: (n: number) => string }> = {
  saved:      { cls: "bg-green-100 text-green-700", label: () => "Saved" },
  unsaved:    { cls: "bg-amber-100 text-amber-500", label: () => "Unsaved changes" },
  fromAip:    { cls: "bg-info-100 text-info-500", label: () => "From AIP" },
  notStarted: { cls: "bg-slate-100 text-slate-600", label: () => "Not started" },
  error:      { cls: "bg-danger-100 text-danger-500", label: (n) => `${n} error${n === 1 ? "" : "s"} — not saved` },
};

export function SectionStatusPill({ status, errorCount = 0 }: { status: SectionStatus; errorCount?: number }) {
  const p = PILL[status];
  return (
    <span className={`inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium whitespace-nowrap ${p.cls}`}>
      {p.label(errorCount)}
    </span>
  );
}

const btn = "px-3 py-1.5 text-sm font-medium border transition-colors disabled:cursor-not-allowed disabled:opacity-50";
export const primaryBtn = `${btn} border-green-700 bg-green-700 text-white hover:bg-green-800`;
export const secondaryBtn = `${btn} border-slate-300 bg-white text-slate-600 hover:bg-slate-50`;

export default function ProposalSectionCard({
  def, status, errorCount, readOnly, dirty, saving, conflict, collapsible, collapsed, summary,
  onToggle, onSave, onDiscard, onReload, onDismissConflict, footerStart, footerEnd, children,
}: {
  def: SectionDef;
  status: SectionStatus;
  errorCount: number;
  readOnly: boolean;
  dirty: boolean;
  saving: boolean;
  /** The stale-version message for this section's last save, or null. */
  conflict: string | null;
  collapsible: boolean;
  collapsed: boolean;
  summary: string;
  onToggle: () => void;
  onSave: () => void;
  onDiscard: () => void;
  onReload: () => void;
  onDismissConflict: () => void;
  /** Focus mode's "← previous" and "next →". */
  footerStart?: ReactNode;
  footerEnd?: ReactNode;
  children: ReactNode;
}) {
  const canSave = !readOnly && !def.fromAip;
  const headingId = `section-${def.key}-heading`;

  return (
    <section aria-labelledby={headingId} className="border border-slate-200 bg-white" data-section={def.key}>
      <header className="flex flex-wrap items-center justify-between gap-2 border-b border-slate-200 px-4 py-3">
        <div className="flex min-w-0 items-center gap-2">
          {collapsible && (
            <button type="button" onClick={onToggle} aria-expanded={!collapsed} aria-controls={`section-${def.key}-body`}
              className="w-5 text-slate-600 hover:text-slate-800" title={collapsed ? "Expand" : "Collapse"}>
              {collapsed ? "▸" : "▾"}
            </button>
          )}
          <h2 id={headingId} className="text-sm font-semibold text-slate-800">
            {def.key === "S" ? def.title : `${def.key}. ${def.title}`}
          </h2>
          <SectionStatusPill status={status} errorCount={errorCount} />
        </div>
        {collapsed && (
          <div className="flex min-w-0 flex-1 items-center justify-end gap-3">
            <span className="truncate text-xs text-slate-600">{summary}</span>
            {canSave && dirty && (
              <button type="button" onClick={onSave} disabled={saving} className={primaryBtn}>
                {saving ? "Saving…" : "Save"}
              </button>
            )}
          </div>
        )}
      </header>

      {!collapsed && (
        <>
          <div id={`section-${def.key}-body`} className="px-4 py-4">{children}</div>

          {conflict && (
            <div role="alert" className="border-t border-amber-300 bg-amber-50 px-4 py-3 text-sm text-amber-900">
              <p>{conflict}</p>
              <div className="mt-2 flex gap-2">
                <button type="button" onClick={onReload} className={secondaryBtn}>Reload</button>
                <button type="button" onClick={onDismissConflict} className={secondaryBtn}>Keep editing</button>
              </div>
            </div>
          )}

          {(canSave || footerStart || footerEnd) && (
            <footer className={`flex flex-wrap items-center justify-between gap-2 border-t px-4 py-3 ${
              conflict ? "border-amber-300 bg-amber-50" : "border-slate-200 bg-slate-50"
            }`}>
              <div>{footerStart}</div>
              <div className="flex flex-wrap items-center gap-2">
                {canSave && (
                  <>
                    <button type="button" onClick={onDiscard} disabled={!dirty || saving} className={secondaryBtn}>Discard</button>
                    <button type="button" onClick={onSave} disabled={!dirty || saving} className={primaryBtn}>
                      {saving ? "Saving…" : "Save section"}
                    </button>
                  </>
                )}
                {footerEnd}
              </div>
            </footer>
          )}
        </>
      )}
    </section>
  );
}

/** A section-shaped placeholder while the proposal loads (spec §6.2 "Loading"). */
export function SectionSkeleton({ shape }: { shape: "summary" | "text" | "table" }) {
  const bar = (w: string, key: number) => <div key={key} className="h-4 animate-pulse bg-slate-100" style={{ width: w }} />;
  return (
    <div className="border border-slate-200 bg-white">
      <div className="border-b border-slate-200 px-4 py-3">{bar("40%", 0)}</div>
      <div className="space-y-3 px-4 py-4">
        {shape === "summary" && Array.from({ length: 6 }, (_, i) => (
          <div key={i} className="grid grid-cols-[13rem_1fr] gap-3">{bar("70%", 1)}{bar(`${50 + (i * 13) % 40}%`, 2)}</div>
        ))}
        {shape === "text" && [bar("95%", 1), bar("90%", 2), bar("60%", 3)]}
        {shape === "table" && Array.from({ length: 4 }, (_, i) => (
          <div key={i} className="grid grid-cols-5 gap-3">{[0, 1, 2, 3, 4].map((c) => bar(`${60 + ((i + c) * 11) % 35}%`, c))}</div>
        ))}
      </div>
    </div>
  );
}
