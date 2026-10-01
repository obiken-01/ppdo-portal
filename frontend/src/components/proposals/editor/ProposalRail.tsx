"use client";

/**
 * The section rail (PPDO-160, spec §6.2 "Left rail", wireframe 1): A–M and Signatories, each with a
 * status dot, and the legend under it. The rail is how a reader moves between sections; in focus
 * mode the page's leave guard runs before the move when the current section has unsaved edits.
 */

import { SECTIONS, sectionLetter, type SectionKey } from "@/lib/proposal-editor";
import type { SectionStatus } from "./ProposalSectionCard";

const DOT: Record<SectionStatus, string> = {
  saved: "bg-green-600",
  unsaved: "bg-amber-500",
  fromAip: "bg-info-500",
  notStarted: "bg-slate-300",
  error: "bg-danger-500",
};

const LEGEND: [SectionStatus, string][] = [
  ["saved", "Saved"],
  ["unsaved", "Unsaved"],
  ["error", "Has errors"],
  ["fromAip", "From the AIP"],
  ["notStarted", "Not started"],
];

export default function ProposalRail({
  current, statuses, onSelect, loading,
}: {
  current: SectionKey | null;
  statuses: Partial<Record<SectionKey, SectionStatus>>;
  onSelect: (key: SectionKey) => void;
  loading: boolean;
}) {
  return (
    <nav aria-label="Proposal sections" className="border border-slate-200 bg-white">
      <p className="border-b border-slate-200 px-3 py-2 text-xs font-semibold uppercase tracking-wide text-slate-600">Sections</p>
      <ol>
        {SECTIONS.map((s) => {
          const status = statuses[s.key];
          const active = current === s.key;
          return (
            <li key={s.key}>
              <button
                type="button"
                onClick={() => onSelect(s.key)}
                disabled={loading}
                aria-current={active ? "step" : undefined}
                className={`flex w-full items-center gap-2 border-l-2 px-3 py-1.5 text-left text-sm transition-colors ${
                  active ? "border-green-700 bg-green-50 font-medium text-slate-800" : "border-transparent text-slate-600 hover:bg-slate-50"
                }`}
              >
                <span className="w-4 text-xs font-semibold text-slate-600">{sectionLetter(s.key)}</span>
                <span className="min-w-0 flex-1 truncate">{s.short}</span>
                <span
                  className={`h-2 w-2 shrink-0 rounded-full ${status ? DOT[status] : "bg-slate-200"}`}
                  title={status ? LEGEND.find(([k]) => k === status)?.[1] : undefined}
                />
              </button>
            </li>
          );
        })}
      </ol>
      <ul className="space-y-1 border-t border-slate-200 px-3 py-2 text-xs text-slate-600">
        {LEGEND.map(([k, label]) => (
          <li key={k} className="flex items-center gap-2">
            <span className={`h-2 w-2 rounded-full ${DOT[k]}`} />
            {label}
          </li>
        ))}
      </ul>
    </nav>
  );
}
