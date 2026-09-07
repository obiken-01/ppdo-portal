"use client";

/**
 * Shared presentational cells for the AIP hierarchy grid.
 *
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour and no styling change.
 *
 * ⚠️ The two className strings below are deliberately plain strings, not a variant helper.
 * Every AIP row renders through these, and the grid they style is the one that prints -- so a
 * "tidier" abstraction here is a restyle of the document, not a refactor. Leave them.
 */

import { fmt, toDisplayUnits } from "@/lib/aip-units";

// ── Chevron ────────────────────────────────────────────────────────────────────

export function Chevron({ open, className = "" }: { open: boolean; className?: string }) {
  return (
    <svg viewBox="0 0 12 12" width="10" height="10"
      className={`inline-block shrink-0 transition-transform duration-100 ${open ? "rotate-90" : ""} ${className}`}
      fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round"
    >
      <polyline points="4,2 8,6 4,10" />
    </svg>
  );
}

// ── Status badge ──────────────────────────────────────────────────────────────

export function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Final"  ? "bg-green-100 text-green-700" :
    status === "Draft"  ? "bg-amber-100 text-amber-700" :
                          "bg-slate-100 text-slate-600";
  return <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>;
}

// ── Table header cell ──────────────────────────────────────────────────────────

export function TH({ children, align = "left", rowSpan, colSpan }: {
  children: React.ReactNode;
  align?: "left" | "right" | "center";
  rowSpan?: number;
  colSpan?: number;
}) {
  const a = align === "right" ? "text-right" : align === "center" ? "text-center" : "text-left";
  return (
    <th rowSpan={rowSpan} colSpan={colSpan}
      className={`px-2 py-2 text-[10px] font-bold uppercase tracking-wide text-slate-600 whitespace-nowrap border-b border-slate-300 bg-slate-100 ${a}`}
    >
      {children}
    </th>
  );
}

// ── Amount cell ────────────────────────────────────────────────────────────────

export function AmtTD({ value, bold = false, white = false }: { value: number | null | undefined; bold?: boolean; white?: boolean }) {
  return (
    <td className={`px-2 py-1.5 text-right text-xs tabular-nums whitespace-nowrap ${
      white ? "text-white font-semibold" : bold ? "font-semibold text-slate-800" : "text-slate-600"
    }`}>
      {fmt(toDisplayUnits(value))}
    </td>
  );
}

export const selectCls = "border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600";
export const inputCls  = "border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-600 w-full focus:outline-none focus:ring-1 focus:ring-green-600";
