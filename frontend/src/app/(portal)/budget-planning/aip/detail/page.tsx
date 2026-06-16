"use client";

/**
 * AIP Detail page — read-only hierarchy view.
 * Route: /budget-planning/aip/detail?id=<aipRecordId>
 *
 * Uses a query param instead of a dynamic [id] segment because
 * Next.js output: 'export' requires all path segments to be known at build time.
 *
 * Access: canAccessBudgetPlanning.
 */

import { useEffect, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import api from "@/lib/api";
import { getAipById, aipErrorMessage } from "@/lib/aip";
import type {
  AipRecordDetail,
  AipOfficeDetail,
  AipActivityDetail,
  MeResponse,
} from "@/types";

// ── Number helpers ─────────────────────────────────────────────────────────────

function fmt(n: number | null | undefined): string {
  if (n == null || n === 0) return "—";
  return n.toLocaleString("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  });
}

function sumActivities(
  office: AipOfficeDetail,
  field: keyof Pick<AipActivityDetail, "ps" | "mooe" | "co" | "total">
): number {
  return office.programs
    .flatMap((p) => p.projects)
    .flatMap((p) => p.activities)
    .reduce((s, a) => s + (a[field] ?? 0), 0);
}

// ── Status badge ──────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === "Final"
      ? "bg-green-100 text-green-700"
      : status === "Draft"
      ? "bg-amber-100 text-amber-700"
      : "bg-slate-100 text-slate-500";
  return (
    <span className={`px-2 py-0.5 text-xs font-medium ${cls}`}>{status}</span>
  );
}

// ── Table column header ────────────────────────────────────────────────────────

function TH({
  children,
  align = "left",
  className = "",
  rowSpan,
  colSpan,
}: {
  children: React.ReactNode;
  align?: "left" | "right" | "center";
  className?: string;
  rowSpan?: number;
  colSpan?: number;
}) {
  const alignCls =
    align === "right"
      ? "text-right"
      : align === "center"
      ? "text-center"
      : "text-left";
  return (
    <th
      rowSpan={rowSpan}
      colSpan={colSpan}
      className={`px-2 py-2 text-[10px] font-bold uppercase tracking-wide text-slate-600 whitespace-nowrap border-b border-slate-300 bg-slate-100 ${alignCls} ${className}`}
    >
      {children}
    </th>
  );
}

// ── Amount cell ────────────────────────────────────────────────────────────────

function AmtTD({
  value,
  bold = false,
}: {
  value: number | null | undefined;
  bold?: boolean;
}) {
  return (
    <td
      className={`px-2 py-1.5 text-right text-xs tabular-nums whitespace-nowrap ${
        bold ? "font-semibold" : "text-slate-700"
      }`}
    >
      {fmt(value)}
    </td>
  );
}

// ── Sector ordering ────────────────────────────────────────────────────────────

const SECTOR_ORDER = ["GENERAL", "SOCIAL", "ECONOMIC", "OTHERS"];

function groupBySector(
  offices: AipOfficeDetail[]
): [string, AipOfficeDetail[]][] {
  const map = new Map<string, AipOfficeDetail[]>();
  for (const o of offices) {
    const sector = (o.sector ?? "OTHERS").toUpperCase();
    if (!map.has(sector)) map.set(sector, []);
    map.get(sector)!.push(o);
  }
  const result: [string, AipOfficeDetail[]][] = [];
  for (const s of SECTOR_ORDER) {
    if (map.has(s)) result.push([s, map.get(s)!]);
  }
  for (const [s, list] of Array.from(map.entries())) {
    if (!SECTOR_ORDER.includes(s)) result.push([s, list]);
  }
  return result;
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function AipDetailPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const id = parseInt(searchParams.get("id") ?? "", 10);

  const [me, setMe] = useState<MeResponse | null>(null);
  const [record, setRecord] = useState<AipRecordDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api.get<MeResponse>("/auth/me").then(({ data }) => {
      if (!data.canAccessBudgetPlanning) {
        router.replace("/dashboard");
        return;
      }
      setMe(data);
    });
  }, [router]);

  useEffect(() => {
    if (!me || isNaN(id)) return;
    setLoading(true);
    setError(null);
    getAipById(id)
      .then(setRecord)
      .catch((err) =>
        setError(aipErrorMessage(err, "Failed to load AIP record."))
      )
      .finally(() => setLoading(false));
  }, [me, id]);

  if (!me) return null;

  if (isNaN(id))
    return (
      <div className="p-8">
        <p className="text-red-600 text-sm mb-3">Invalid AIP record ID.</p>
        <Link
          href="/budget-planning/aip"
          className="text-sm text-green-700 hover:underline"
        >
          ← Back to AIP list
        </Link>
      </div>
    );

  if (loading)
    return (
      <div className="p-8 text-slate-500 text-sm">Loading AIP record…</div>
    );

  if (error || !record)
    return (
      <div className="p-8">
        <p className="text-red-600 text-sm mb-3">
          {error ?? "Record not found."}
        </p>
        <Link
          href="/budget-planning/aip"
          className="text-sm text-green-700 hover:underline"
        >
          ← Back to AIP list
        </Link>
      </div>
    );

  const sectors = groupBySector(record.offices);

  const programCount = record.offices.flatMap((o) => o.programs).length;
  const projectCount = record.offices
    .flatMap((o) => o.programs)
    .flatMap((p) => p.projects).length;
  const activityCount = record.offices
    .flatMap((o) => o.programs)
    .flatMap((p) => p.projects)
    .flatMap((p) => p.activities).length;

  return (
    <div className="p-6 max-w-screen-2xl mx-auto">
      {/* ── Header ─────────────────────────────────────────────────── */}
      <div className="flex items-start justify-between mb-4 gap-4">
        <div>
          <div className="flex items-center gap-3 mb-1">
            <h1 className="text-xl font-bold text-slate-800">
              Annual Investment Program — FY {record.fiscalYear}
            </h1>
            <StatusBadge status={record.status} />
          </div>
          <p className="text-sm text-slate-500">
            {record.offices.length} office records &middot; {programCount}{" "}
            programs &middot; {projectCount} projects &middot; {activityCount}{" "}
            activities
          </p>
          {record.originalFilename && (
            <p className="text-xs text-slate-400 mt-0.5">
              Source: {record.originalFilename}
            </p>
          )}
        </div>
        <Link
          href="/budget-planning/aip"
          className="text-sm text-green-700 hover:underline whitespace-nowrap"
        >
          ← Back to AIP list
        </Link>
      </div>

      {/* ── Table ──────────────────────────────────────────────────── */}
      <div className="overflow-x-auto border border-slate-200 shadow-sm">
        <table className="min-w-[1800px] w-full border-collapse text-sm">
          <colgroup>
            <col style={{ width: "140px" }} />
            <col style={{ width: "300px" }} />
            <col style={{ width: "56px" }} />
            <col style={{ width: "110px" }} />
            <col style={{ width: "72px" }} />
            <col style={{ width: "72px" }} />
            <col style={{ width: "200px" }} />
            <col style={{ width: "90px" }} />
            <col style={{ width: "90px" }} />
            <col style={{ width: "90px" }} />
            <col style={{ width: "90px" }} />
            <col style={{ width: "100px" }} />
            <col style={{ width: "80px" }} />
            <col style={{ width: "80px" }} />
            <col style={{ width: "70px" }} />
          </colgroup>

          <thead>
            <tr className="bg-slate-100">
              <TH rowSpan={2}>AIP Ref Code</TH>
              <TH rowSpan={2}>Program / Project / Activity Description</TH>
              <TH rowSpan={2} align="center">
                eSRE Code
              </TH>
              <TH rowSpan={2}>Implementing Office</TH>
              <TH colSpan={2} align="center">
                Schedule of Implementation
              </TH>
              <TH rowSpan={2}>Expected Outputs</TH>
              <TH rowSpan={2} align="center">
                Funding Source
              </TH>
              <TH colSpan={4} align="center">
                Amount (in ₱000)
              </TH>
              <TH colSpan={3} align="center">
                CC Expenditure (₱000)
              </TH>
            </tr>
            <tr className="bg-slate-100">
              <TH align="center">Start</TH>
              <TH align="center">End</TH>
              <TH align="right">PS</TH>
              <TH align="right">MOOE</TH>
              <TH align="right">CO</TH>
              <TH align="right">Total</TH>
              <TH align="right">Adaptation</TH>
              <TH align="right">Mitigation</TH>
              <TH align="center">CC Code</TH>
            </tr>
          </thead>

          <tbody>
            {sectors.map(([sector, offices]) => (
              <>
                <tr key={`sector-${sector}`} className="bg-green-800">
                  <td
                    colSpan={15}
                    className="px-3 py-2 text-white font-bold text-xs tracking-widest uppercase"
                  >
                    {sector} SECTOR
                  </td>
                </tr>

                {offices.map((office) => {
                  const officePs = sumActivities(office, "ps");
                  const officeMooe = sumActivities(office, "mooe");
                  const officeCo = sumActivities(office, "co");
                  const officeTotal = sumActivities(office, "total");

                  return (
                    <>
                      <tr
                        key={`office-${office.id}`}
                        className="bg-green-50 border-t-2 border-green-200"
                      >
                        <td className="px-2 py-2 font-mono text-xs text-slate-500 align-top">
                          {office.refCode}
                        </td>
                        <td className="px-2 py-2 font-bold text-sm uppercase text-green-900 align-top">
                          {office.name}
                        </td>
                        <td />
                        <td />
                        <td />
                        <td />
                        <td />
                        <td />
                        <AmtTD value={officePs} bold />
                        <AmtTD value={officeMooe} bold />
                        <AmtTD value={officeCo} bold />
                        <AmtTD value={officeTotal} bold />
                        <td />
                        <td />
                        <td />
                      </tr>

                      {office.programs.map((prog) => (
                        <>
                          <tr
                            key={`prog-${prog.id}`}
                            className="bg-slate-50 border-t border-slate-200"
                          >
                            <td className="px-2 py-1.5 pl-5 font-mono text-xs text-slate-400">
                              {prog.refCode}
                            </td>
                            <td
                              colSpan={14}
                              className="px-2 py-1.5 pl-5 font-semibold text-xs italic text-slate-700 uppercase"
                            >
                              {prog.name}
                            </td>
                          </tr>

                          {prog.projects.map((proj) => (
                            <>
                              <tr
                                key={`proj-${proj.id}`}
                                className="border-t border-slate-100"
                              >
                                <td className="px-2 py-1.5 pl-9 font-mono text-xs text-slate-400">
                                  {proj.refCode}
                                </td>
                                <td
                                  colSpan={14}
                                  className="px-2 py-1.5 pl-9 text-xs font-medium text-slate-600"
                                >
                                  {proj.name}
                                </td>
                              </tr>

                              {proj.activities.map((act) => (
                                <tr
                                  key={`act-${act.id}`}
                                  className="border-t border-slate-100 hover:bg-blue-50 transition-colors"
                                >
                                  <td className="px-2 py-1.5 pl-12 font-mono text-[11px] text-slate-400 align-top">
                                    {act.refCode}
                                  </td>
                                  <td className="px-2 py-1.5 pl-12 text-xs text-slate-800 align-top leading-snug">
                                    {act.name}
                                  </td>
                                  <td className="px-2 py-1.5 text-center text-xs text-slate-600">
                                    {act.esreCode ?? "—"}
                                  </td>
                                  <td className="px-2 py-1.5 text-xs text-slate-600 align-top">
                                    {act.implementingOffice ?? "—"}
                                  </td>
                                  <td className="px-2 py-1.5 text-center text-xs text-slate-600 whitespace-nowrap">
                                    {act.startDate ?? "—"}
                                  </td>
                                  <td className="px-2 py-1.5 text-center text-xs text-slate-600 whitespace-nowrap">
                                    {act.endDate ?? "—"}
                                  </td>
                                  <td className="px-2 py-1.5 text-xs text-slate-600 align-top leading-snug">
                                    {act.expectedOutputs ?? "—"}
                                  </td>
                                  <td className="px-2 py-1.5 text-center text-xs font-medium text-slate-700">
                                    {act.fundingSourceSnapshot ?? "—"}
                                  </td>
                                  <AmtTD value={act.ps} />
                                  <AmtTD value={act.mooe} />
                                  <AmtTD value={act.co} />
                                  <AmtTD value={act.total} />
                                  <AmtTD value={act.ccAdaptation} />
                                  <AmtTD value={act.ccMitigation} />
                                  <td className="px-2 py-1.5 text-center text-xs text-slate-500">
                                    {act.ccTypologyCode ?? "—"}
                                  </td>
                                </tr>
                              ))}
                            </>
                          ))}
                        </>
                      ))}
                    </>
                  );
                })}
              </>
            ))}
          </tbody>
        </table>
      </div>

      <p className="text-xs text-slate-400 mt-2 text-right">
        {activityCount} activities across {record.offices.length} office records
      </p>
    </div>
  );
}
