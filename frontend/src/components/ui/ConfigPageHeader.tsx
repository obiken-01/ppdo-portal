"use client";

/**
 * ConfigPageHeader — shared title/description/actions header (RAL-201),
 * adopted portal-wide (RAL-206 item 5) beyond its original Config-only use
 * (Accounts, Offices, Divisions, Funding Sources, Procurement Presets, Price
 * Index, Audit Log). The name is historical, not a scope restriction.
 *
 * Stacks the action buttons below the title/description on narrow viewports
 * instead of forcing everything onto one non-wrapping row — the single
 * un-wrapped `flex items-start justify-between` row every Config page used to
 * duplicate crushed the description into a sliver at mobile widths.
 *
 * `title`/`description` accept `ReactNode`, not just `string`, so a page that
 * renders a badge next to its title (e.g. a status pill) doesn't have to give
 * that up to adopt this component.
 */

export default function ConfigPageHeader({
  title,
  description,
  actions,
}: {
  title: React.ReactNode;
  description: React.ReactNode;
  actions?: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h1 className="text-xl font-bold text-slate-800">{title}</h1>
        <p className="text-sm text-slate-600">{description}</p>
      </div>
      {actions && (
        <div className="flex flex-wrap items-center gap-2 sm:shrink-0">{actions}</div>
      )}
    </div>
  );
}
