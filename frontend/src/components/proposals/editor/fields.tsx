"use client";

/**
 * Small field primitives for the proposal editor's sections (PPDO-160).
 *
 * ⚠️ **Read-only renders text, not disabled inputs** (spec §6.2 "Final" and "Read-only"): a Final
 * proposal or a cross-office reviewer sees the document, not a greyed-out form. Every primitive here
 * takes `readOnly` and switches itself, so a section never has to remember to.
 */

import type { ReactNode } from "react";

export const inputCls =
  "w-full border border-slate-300 bg-white px-2 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600";

const invalidCls = " border-red-600";

/** The section's helper text, from the template's italic guidance (decision 21). */
export function Guidance({ children }: { children: ReactNode }) {
  return <p className="mb-3 text-xs leading-relaxed text-slate-600">{children}</p>;
}

/** Field errors from the server, under the field, in red-600 (spec §6.2 "Validation"). */
export function FieldErrors({ messages }: { messages?: string[] }) {
  if (!messages?.length) return null;
  return (
    <p className="mt-1 text-xs text-red-600" data-field-error>
      {messages.join(" ")}
    </p>
  );
}

function ReadText({ value, multiline }: { value: string | null | undefined; multiline?: boolean }) {
  if (value == null || value.trim() === "") return <span className="text-sm text-slate-600">—</span>;
  return <span className={`text-sm text-slate-800${multiline ? " whitespace-pre-line" : ""}`}>{value}</span>;
}

export function TextInput({
  value, onChange, readOnly, errors, placeholder, ariaLabel, id,
}: {
  value: string | null;
  onChange: (v: string | null) => void;
  readOnly: boolean;
  errors?: string[];
  placeholder?: string;
  ariaLabel?: string;
  id?: string;
}) {
  if (readOnly) return <ReadText value={value} />;
  return (
    <>
      <input
        id={id}
        type="text"
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value === "" ? null : e.target.value)}
        placeholder={placeholder}
        aria-label={ariaLabel}
        aria-invalid={errors?.length ? true : undefined}
        className={inputCls + (errors?.length ? invalidCls : "")}
      />
      <FieldErrors messages={errors} />
    </>
  );
}

export function TextArea({
  value, onChange, readOnly, errors, placeholder, ariaLabel, rows = 2,
}: {
  value: string | null;
  onChange: (v: string | null) => void;
  readOnly: boolean;
  errors?: string[];
  placeholder?: string;
  ariaLabel?: string;
  rows?: number;
}) {
  if (readOnly) return <ReadText value={value} multiline />;
  return (
    <>
      <textarea
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value === "" ? null : e.target.value)}
        placeholder={placeholder}
        aria-label={ariaLabel}
        aria-invalid={errors?.length ? true : undefined}
        rows={rows}
        // The house rule for long text cells: min 44px, max 88px, resize vertical (CLAUDE.md).
        className={`${inputCls} min-h-[44px] max-h-[88px] resize-y${errors?.length ? invalidCls : ""}`}
      />
      <FieldErrors messages={errors} />
    </>
  );
}

/** A whole-number count (beneficiaries). Blank stays blank — a label-only row is allowed. */
export function CountInput({
  value, onChange, readOnly, errors, ariaLabel,
}: {
  value: number | null;
  onChange: (v: number | null) => void;
  readOnly: boolean;
  errors?: string[];
  ariaLabel: string;
}) {
  if (readOnly) return <span className="text-sm tabular-nums text-slate-800">{value ?? ""}</span>;
  return (
    <>
      <input
        type="number"
        inputMode="numeric"
        min={0}
        step={1}
        value={value ?? ""}
        onChange={(e) => onChange(e.target.value === "" ? null : Number(e.target.value))}
        aria-label={ariaLabel}
        aria-invalid={errors?.length ? true : undefined}
        className={`${inputCls} w-24 text-right tabular-nums${errors?.length ? invalidCls : ""}`}
      />
      <FieldErrors messages={errors} />
    </>
  );
}

/** "From AIP" — an automatic, read-only value (spec §6.2). */
export function FromAipTag() {
  return (
    <span className="ml-1.5 inline-flex items-center rounded-full bg-info-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-info-500">
      From AIP
    </span>
  );
}

export function ProposalOnlyTag() {
  return (
    <span className="ml-1.5 inline-flex items-center rounded-full bg-amber-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-amber-500">
      Proposal only
    </span>
  );
}

export function AddRowButton({ onClick, label = "+ Add row" }: { onClick: () => void; label?: string }) {
  return (
    <button type="button" onClick={onClick} className="mt-2 text-sm font-medium text-green-700 hover:underline">
      {label}
    </button>
  );
}

/** A small text button for a table row (Remove, ↑, ↓). */
export function RowButton({
  onClick, label, title, disabled, danger,
}: {
  onClick: () => void;
  label: string;
  title: string;
  disabled?: boolean;
  danger?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      title={title}
      aria-label={title}
      disabled={disabled}
      className={`border px-1.5 py-0.5 text-xs disabled:cursor-not-allowed disabled:opacity-40 ${
        danger
          ? "border-danger-500/30 text-danger-500 hover:bg-danger-100"
          : "border-slate-300 text-slate-600 hover:bg-slate-100"
      }`}
    >
      {label}
    </button>
  );
}

/** A flat table with the portal's header style. */
export function Table({ headers, children, minWidth }: { headers: ReactNode[]; children: ReactNode; minWidth?: number }) {
  return (
    <div className="overflow-x-auto">
      <table className="w-full border-collapse text-sm" style={minWidth ? { minWidth } : undefined}>
        <thead>
          <tr className="border-b border-slate-200 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-600">
            {headers.map((h, i) => <th key={i} className="px-2 py-2 align-bottom">{h}</th>)}
          </tr>
        </thead>
        <tbody>{children}</tbody>
      </table>
    </div>
  );
}

export const tdCls = "border-b border-slate-100 px-2 py-1.5 align-top";

/** A label/value row of Section A's summary table. */
export function SummaryRow({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-1 gap-1 border-b border-slate-100 py-2 sm:grid-cols-[13rem_1fr] sm:gap-3">
      <div className="text-sm font-semibold text-slate-800">{label}</div>
      <div className="min-w-0">{children}</div>
    </div>
  );
}

/** Replaces item `i` of `list`. */
export function replaceAt<T>(list: T[], i: number, item: T): T[] {
  return list.map((x, k) => (k === i ? item : x));
}

/** `list` without item `i`. */
export function removeAt<T>(list: T[], i: number): T[] {
  return list.filter((_, k) => k !== i);
}

/** Moves item `i` by `delta` (−1 up, +1 down); a move past either end is ignored. */
export function move<T>(list: T[], i: number, delta: number): T[] {
  const j = i + delta;
  if (j < 0 || j >= list.length) return list;
  const next = [...list];
  [next[i], next[j]] = [next[j], next[i]];
  return next;
}
