"use client";

/**
 * Drill-down ⇄ Full office, on AIP Review (PPDO-94, spec decision 1).
 *
 * ⚠️ Two views, one screen — navigation and overview are different jobs and neither subsumes the
 * other (spec §2.1). Precedent: PPDO-78's Board/Table switch on the dashboard Offices band.
 */

export type AipReviewView = "drilldown" | "full";

export default function AipReviewViewSwitch({
  view,
  onChange,
}: {
  view: AipReviewView;
  onChange: (view: AipReviewView) => void;
}) {
  return (
    <div className="inline-flex border border-slate-300 bg-white text-xs font-medium" role="group" aria-label="View">
      <ViewButton view={view} value="drilldown" onChange={onChange}>Drill-down</ViewButton>
      <ViewButton view={view} value="full" onChange={onChange}>Full office</ViewButton>
    </div>
  );
}

function ViewButton({
  view, value, onChange, children,
}: {
  view: AipReviewView;
  value: AipReviewView;
  onChange: (view: AipReviewView) => void;
  children: React.ReactNode;
}) {
  const active = view === value;
  return (
    <button
      type="button"
      aria-pressed={active}
      onClick={() => onChange(value)}
      className={`px-3 py-1.5 ${value === "drilldown" ? "border-r border-slate-300" : ""} ${
        active ? "bg-green-700 text-white" : "text-slate-600 hover:bg-slate-50"
      }`}
    >
      {children}
    </button>
  );
}
