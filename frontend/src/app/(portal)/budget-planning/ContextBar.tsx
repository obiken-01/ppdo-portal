"use client";

/**
 * ContextBar — the three axes that decide what the dashboard shows (PPDO-20).
 *
 * Fiscal year, office and division. All three are stated because all three change what is on the
 * page, and a reader who cannot see which office they are looking at cannot trust any figure on
 * it. A **locked** field — dashed border, no chevron — is an axis this account cannot change; it
 * still says what the value IS, which is the job.
 *
 * ⚠️ **A guest office gets no division field at all**, not a locked one. Division does not narrow
 * a guest office (`Permission_Matrix.md` §3.1), so rendering an inert control there would imply it
 * might. This replaced an earlier "Seeing: RMED only" chip, which read as jargon.
 */

export function LockedField({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center gap-2">
      <span className="text-xs font-semibold text-slate-600 uppercase tracking-wide">{label}</span>
      <span
        className="text-sm text-slate-600 bg-white border border-dashed border-slate-300 px-3 py-1.5"
        title="You cannot change this"
      >
        {value}
      </span>
    </div>
  );
}

export default function ContextBar({
  fiscalYear,
  availableFiscalYears,
  onFiscalYearChange,
  fiscalYearDisabled,
  officeField,
  divisionField,
}: {
  fiscalYear: number | null;
  availableFiscalYears: number[];
  onFiscalYearChange: (fy: number) => void;
  fiscalYearDisabled?: boolean;
  /** Locked label, or a picker supplied by the caller when the account may choose. */
  officeField: React.ReactNode;
  /** Omit entirely for a guest office — see the component doc comment. */
  divisionField?: React.ReactNode;
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
    <div className="flex flex-wrap items-center gap-x-5 gap-y-2">
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

      {officeField}
      {divisionField}
    </div>
  );
}
