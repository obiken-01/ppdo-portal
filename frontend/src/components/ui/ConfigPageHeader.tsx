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
 *
 * `busy` (PPDO-281) disables every action while the page's main data loads, so "+ Add …" can't
 * be pressed before the list it adds to has arrived. The actions sit in a <fieldset>, and a
 * disabled fieldset disables all the buttons and inputs inside it; the layout does not move.
 */

export default function ConfigPageHeader({
  title,
  description,
  actions,
  busy = false,
}: {
  title: React.ReactNode;
  description: React.ReactNode;
  actions?: React.ReactNode;
  /** The page's main data is still loading: disable the actions until it arrives. */
  busy?: boolean;
}) {
  return (
    <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
      <div>
        <h1 className="text-xl font-bold text-slate-800">{title}</h1>
        <p className="text-sm text-slate-600">{description}</p>
      </div>
      {actions && (
        <fieldset
          disabled={busy}
          className="m-0 p-0 border-0 min-w-0 flex flex-wrap items-center gap-2 sm:shrink-0 [&_:disabled]:opacity-50 [&_:disabled]:cursor-not-allowed"
        >
          {actions}
        </fieldset>
      )}
    </div>
  );
}
