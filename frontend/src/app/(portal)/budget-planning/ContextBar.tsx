"use client";

/**
 * ContextBar — the fiscal-year picker of the Investment Planning dashboard (PPDO-20).
 *
 * It used to be a row of three axes — fiscal year, locked Office, locked Division — under a header
 * line that already said the office. PPDO-179 (F8): office and division now live once, in the page's
 * header line, and the picker sits in the header's action slot. The one axis a reader can actually
 * change is the only one that is a control.
 */

export default function ContextBar({
  fiscalYear,
  availableFiscalYears,
  onFiscalYearChange,
  fiscalYearDisabled,
}: {
  fiscalYear: number | null;
  availableFiscalYears: number[];
  onFiscalYearChange: (fy: number) => void;
  fiscalYearDisabled?: boolean;
}) {
  // A year the user has selected but which is not in the list (e.g. the default resolved
  // server-side before the list arrived) still has to render, or the select collapses to blank.
  const years =
    availableFiscalYears.length > 0
      ? availableFiscalYears
      : fiscalYear != null
      ? [fiscalYear]
      : [];

  return (
    <div className="flex items-center gap-2">
      <label
        htmlFor="bp-fiscal-year"
        className="text-xs font-semibold text-slate-600 uppercase tracking-wide"
      >
        Fiscal Year
      </label>
      <select
        id="bp-fiscal-year"
        className="border border-slate-200 bg-white text-sm text-slate-600 px-3 py-1.5 focus:outline-none focus:ring-1 focus:ring-green-500 disabled:opacity-50 disabled:cursor-not-allowed"
        value={fiscalYear ?? ""}
        onChange={(e) => onFiscalYearChange(Number(e.target.value))}
        disabled={fiscalYearDisabled}
      >
        {years.map((fy) => (
          <option key={fy} value={fy}>
            FY {fy}
          </option>
        ))}
      </select>
    </div>
  );
}
