"use client";

/**
 * ConfigPageHeader — shared title/description/actions header for Config pages
 * (Accounts, Offices, Divisions, Funding Sources, Procurement Presets, Price
 * Index, Audit Log) (RAL-201).
 *
 * Stacks the action buttons below the title/description on narrow viewports
 * instead of forcing everything onto one non-wrapping row — the single
 * un-wrapped `flex items-start justify-between` row every Config page used to
 * duplicate crushed the description into a sliver at mobile widths.
 */

export default function ConfigPageHeader({
  title,
  description,
  actions,
}: {
  title: string;
  description: string;
  actions?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h1 className="text-lg font-bold text-slate-800">{title}</h1>
        <p className="text-sm text-slate-600">{description}</p>
      </div>
      {actions && (
        <div className="flex flex-wrap items-center gap-2 sm:shrink-0">{actions}</div>
      )}
    </div>
  );
}
