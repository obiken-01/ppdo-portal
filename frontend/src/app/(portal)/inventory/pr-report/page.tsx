"use client";

/**
 * PR Report page — RAL-55.
 * Matches Penpot frame "07 PR Report".
 *
 * Access guard: canAccessInventory OR canAccessReports.
 *
 * Layout:
 *   Toolbar     — PR selector (searchable combobox) + Export Excel button
 *   Section 1   — PR Details (all header fields, read-only)
 *   Section 2   — Line Items table
 *   Section 3   — Distribution table (split delivery rows per delivery event)
 *
 * API endpoints:
 *   GET /api/purchase-requests                    → all PRs for the selector
 *   GET /api/purchase-requests/{id}/report        → PR Report JSON (3 sections)
 *   GET /api/purchase-requests/{id}/export        → .xlsx blob download
 */

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { useToast } from "@/components/ui/Toast";
import type {
  MeResponse,
  PRReportResponse,
  PRSummaryResponse,
} from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------


function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(n);
}

function fmtDate(d: string): string {
  if (!d) return "—";
  // DateOnly from backend: "YYYY-MM-DD" or full ISO string
  return d.slice(0, 10);
}

const STATUS_BADGE: Record<string, string> = {
  Open:                "bg-blue-100 text-blue-700",
  PartiallyDelivered:  "bg-amber-100 text-amber-700",
  FullyDelivered:      "bg-green-100 text-green-700",
  Completed:           "bg-slate-100 text-slate-600",
};

const PR_STATUS_BADGE: Record<string, string> = {
  Open:                "bg-blue-100 text-blue-700 border border-blue-200",
  PartiallyDelivered:  "bg-amber-100 text-amber-700 border border-amber-200",
  FullyDelivered:      "bg-green-100 text-green-700 border border-green-200",
  Completed:           "bg-slate-100 text-slate-600 border border-slate-200",
};

// ---------------------------------------------------------------------------
// Section heading
// ---------------------------------------------------------------------------

function SectionHeading({ number, title }: { number: string; title: string }) {
  return (
    <div className="flex items-center gap-3 px-6 py-3 bg-green-600 text-white rounded-t-lg">
      <span className="w-6 h-6 rounded-full bg-white text-green-700 flex items-center justify-center text-xs font-bold shrink-0">
        {number}
      </span>
      <span className="text-sm font-semibold tracking-wide uppercase">{title}</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Read-only field
// ---------------------------------------------------------------------------

function Field({
  label,
  value,
  span2 = false,
  mono = false,
  pre = false,
}: {
  label: string;
  value: string | null | undefined;
  span2?: boolean;
  mono?: boolean;
  pre?: boolean;
}) {
  return (
    <div className={span2 ? "md:col-span-2" : ""}>
      <p className="text-xs font-medium text-slate-500 mb-1">{label}</p>
      {pre ? (
        <div className="w-full px-3 py-2 text-sm border border-slate-200 bg-cell-auto text-slate-700 min-h-[44px] whitespace-pre-wrap">
          {value || "—"}
        </div>
      ) : (
        <div className={`w-full px-3 py-2 text-sm border border-slate-200 bg-cell-auto ${
          mono ? "font-mono text-slate-800 font-semibold" : "text-slate-700"
        }`}>
          {value || "—"}
        </div>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// PR Combobox (same pattern as Receive Delivery)
// ---------------------------------------------------------------------------

function PRCombobox({
  search, onSearchChange, onFocus, onBlur,
  open, suggestions, selectedId, onSelect, loading,
}: {
  search: string;
  onSearchChange: (v: string) => void;
  onFocus: () => void;
  onBlur: () => void;
  open: boolean;
  suggestions: PRSummaryResponse[];
  selectedId: string;
  onSelect: (pr: PRSummaryResponse) => void;
  loading: boolean;
}) {
  return (
    <div className="relative min-w-80">
      <div className="flex items-center border border-slate-200 bg-white shadow-sm focus-within:ring-2 focus-within:ring-green-600">
        <input
          type="text"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          onFocus={onFocus}
          onBlur={onBlur}
          placeholder={loading ? "Loading PRs…" : "Search PR number, division, status…"}
          disabled={loading}
          className="flex-1 px-3 py-2.5 text-sm bg-transparent outline-none disabled:opacity-60"
        />
        {loading && (
          <span className="px-3">
            <span className="w-4 h-4 border-2 border-slate-300 border-t-transparent rounded-full animate-spin inline-block" />
          </span>
        )}
      </div>

      {open && (
        <ul className="absolute z-50 top-full left-0 right-0 bg-white border border-slate-200 shadow-xl max-h-64 overflow-y-auto text-sm">
          {suggestions.length === 0 ? (
            <li className="px-4 py-3 text-slate-400 text-xs">No PRs match your search.</li>
          ) : suggestions.map((pr) => (
            <li
              key={pr.id}
              onMouseDown={(e) => { e.preventDefault(); onSelect(pr); }}
              className={`px-4 py-2.5 cursor-pointer border-b border-slate-50 last:border-0 transition-colors ${
                pr.id === selectedId ? "bg-green-50" : "hover:bg-slate-50"
              }`}
            >
              <div className="flex items-center gap-2 flex-wrap">
                <span className="font-mono font-semibold text-slate-800 text-xs">{pr.prNo}</span>
                <span className={`text-xs px-1.5 py-0.5 rounded-full font-medium ${STATUS_BADGE[pr.status] ?? "bg-slate-100 text-slate-600"}`}>
                  {pr.status}
                </span>
                <span className="text-xs text-slate-400">{pr.division}</span>
                {pr.id === selectedId && <span className="ml-auto text-green-600 text-xs font-medium">✓ Selected</span>}
              </div>
              <div className="text-xs text-slate-500 mt-0.5 truncate">{pr.requestedBy}</div>
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function PRReportPage() {
  const router    = useRouter();
  const { toast } = useToast();

  const [authChecked, setAuthChecked] = useState(false);

  // PR list + combobox
  const [prs, setPRs]               = useState<PRSummaryResponse[]>([]);
  const [prsLoading, setPRsLoading] = useState(false);
  const [prSearch, setPrSearch]     = useState("");
  const [prOpen, setPrOpen]         = useState(false);

  // Selected PR + report data
  const [selectedId, setSelectedId]     = useState("");
  const [report, setReport]             = useState<PRReportResponse | null>(null);
  const [reportLoading, setReportLoading] = useState(false);

  // Export
  const [exporting, setExporting] = useState(false);

  // ── Computed delivery/distribution totals per item (from Section 3 data) ──
  // Used to populate Section 2 Qty Delivered / Qty Distributed / Remaining.
  //
  // qtyDelivered[itemNo]   = sum of unique (itemNo, deliveryRef) QtyDelivered values
  //                          (deduplicates split-delivery rows that share the same value)
  // qtyDistributed[itemNo] = sum of all QtyIssued for that item (equals qtyDelivered
  //                          when all distributions are fully recorded)

  const { qtyDelivered, qtyDistributed } = useMemo(() => {
    const delivered: Record<number, number>    = {};
    const distributed: Record<number, number>  = {};
    const seen = new Set<string>();

    for (const d of report?.distributions ?? []) {
      // Qty Distributed — straightforward sum of QtyIssued
      distributed[d.itemNo] = (distributed[d.itemNo] ?? 0) + d.qtyIssued;

      // Qty Delivered — one QtyDelivered per (itemNo, deliveryRef) to avoid double-counting
      const key = `${d.itemNo}-${d.deliveryRef}`;
      if (!seen.has(key)) {
        seen.add(key);
        delivered[d.itemNo] = (delivered[d.itemNo] ?? 0) + d.qtyDelivered;
      }
    }
    return { qtyDelivered: delivered, qtyDistributed: distributed };
  }, [report]);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory && !data.canAccessReports) {
          router.replace("/dashboard");
          return;
        }
        setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load PR list ───────────────────────────────────────────────────────────

  useEffect(() => {
    if (!authChecked) return;
    setPRsLoading(true);
    api.get<PRSummaryResponse[]>("/purchase-requests")
      .then(({ data }) => setPRs(data))
      .catch(() => toast.error("Failed to load PRs", "Could not fetch purchase requests."))
      .finally(() => setPRsLoading(false));
  }, [authChecked]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Load report when PR selected ──────────────────────────────────────────

  useEffect(() => {
    if (!selectedId) { setReport(null); return; }
    setReportLoading(true);
    api.get<PRReportResponse>(`/purchase-requests/${selectedId}/report`)
      .then(({ data }) => setReport(data))
      .catch(() => toast.error("Failed to load report", "Could not fetch PR report data."))
      .finally(() => setReportLoading(false));
  }, [selectedId]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── PR combobox helpers ────────────────────────────────────────────────────

  const prSuggestions = useMemo(() => {
    const q = prSearch.toLowerCase().trim();
    if (!q) return prs;
    return prs.filter((p) =>
      p.prNo.toLowerCase().includes(q) ||
      p.division.toLowerCase().includes(q) ||
      p.status.toLowerCase().includes(q) ||
      p.requestedBy.toLowerCase().includes(q)
    );
  }, [prs, prSearch]);

  function selectPR(pr: PRSummaryResponse) {
    setSelectedId(pr.id);
    setPrSearch(pr.prNo);
    setPrOpen(false);
  }

  function clearPR() {
    setSelectedId("");
    setReport(null);
    setPrSearch("");
    setPrOpen(false);
  }

  // ── Export Excel ───────────────────────────────────────────────────────────

  async function handleExport() {
    if (!selectedId || !report) return;
    setExporting(true);
    try {
      const response = await api.get(`/purchase-requests/${selectedId}/export`, {
        responseType: "blob",
      });
      const url  = URL.createObjectURL(response.data as Blob);
      const link = document.createElement("a");
      link.href  = url;
      link.download = `PR_Report_${report.pr.prNo}.xlsx`;
      link.click();
      URL.revokeObjectURL(url);
      toast.success("Export ready", `${link.download} downloaded.`);
    } catch {
      toast.error("Export failed", "Could not generate the Excel report.");
    } finally {
      setExporting(false);
    }
  }

  // ── Guards ─────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  const pr = report?.pr ?? null;

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-5">

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="flex flex-wrap items-center gap-3">
          {/* PR combobox */}
          <PRCombobox
            search={prSearch}
            onSearchChange={(v) => { setPrSearch(v); setPrOpen(true); if (!v) clearPR(); }}
            onFocus={() => setPrOpen(true)}
            onBlur={() => setTimeout(() => setPrOpen(false), 150)}
            open={prOpen && prSuggestions.length > 0}
            suggestions={prSuggestions}
            selectedId={selectedId}
            onSelect={selectPR}
            loading={prsLoading}
          />
          {selectedId && (
            <button
              onClick={clearPR}
              className="text-sm text-slate-400 hover:text-slate-600 transition-colors"
            >
              Clear
            </button>
          )}

          <div className="flex-1" />

          {/* Export Excel */}
          <button
            onClick={handleExport}
            disabled={!selectedId || !report || exporting}
            className="flex items-center gap-2 px-4 py-2.5 text-sm rounded-lg border border-slate-200 bg-white text-slate-700 hover:bg-slate-50 shadow-sm transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
          >
            {exporting
              ? <span className="w-4 h-4 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" />
              : <span>⬇</span>}
            {exporting ? "Exporting…" : "Export to Excel"}
          </button>
        </div>

        {/* ── Empty / loading state ─────────────────────────────────────────── */}
        {!selectedId && (
          <div className="bg-white border border-slate-200 shadow-sm rounded-xl flex flex-col items-center justify-center py-20 gap-3 text-slate-400">
            <span className="text-4xl">📋</span>
            <p className="text-sm font-medium">Select a Purchase Request to view its report</p>
            <p className="text-xs">Search by PR number, division, or status above</p>
          </div>
        )}

        {selectedId && reportLoading && (
          <div className="bg-white border border-slate-200 shadow-sm rounded-xl flex items-center justify-center py-20">
            <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
          </div>
        )}

        {/* ── Report sections ───────────────────────────────────────────────── */}
        {pr && !reportLoading && (
          <>

            {/* ── Section 1 — PR Details ──────────────────────────────────── */}
            <div className="bg-white border border-slate-200 shadow-sm rounded-lg overflow-hidden">
              <SectionHeading number="1" title="PR Details" />
              <div className="p-6 grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-4">

                {/* Row 1 */}
                <Field label="PR No." value={pr.prNo} mono />
                <div>
                  <p className="text-xs font-medium text-slate-500 mb-1">Status</p>
                  <div className="px-3 py-2">
                    <span className={`text-xs font-semibold px-3 py-1 rounded-full ${PR_STATUS_BADGE[pr.status] ?? "bg-slate-100 text-slate-600"}`}>
                      {pr.status}
                    </span>
                  </div>
                </div>

                {/* Row 2 */}
                <Field label="PR Date"    value={fmtDate(pr.prDate)} />
                <Field label="Date Created" value={new Date(pr.dateCreated).toLocaleDateString("en-PH")} />

                {/* Row 3 */}
                <Field label="Department" value={pr.department} />
                <Field label="Division"   value={pr.division} />

                {/* Row 4 */}
                <Field label="Fund"       value={pr.fund} span2 />

                {/* Row 5 */}
                <Field label="Requested By" value={pr.requestedBy} />
                <Field label="Position"     value={pr.position} />

                {/* Row 6 */}
                <Field label="Approved By"       value={pr.approvedBy} />
                <Field label="Approving Position" value={pr.approvingPosition} />

                {/* Row 7 */}
                <Field label="AIP Code"    value={pr.aipCode} />
                <Field label="Account No." value={pr.accountNo} />

                {/* Row 8 */}
                <Field label="Account Title" value={pr.accountTitle} span2 />

                {/* Row 9–11 — long text */}
                <Field label="Program"  value={pr.program}  span2 pre />
                <Field label="Project"  value={pr.project}  span2 pre />
                <Field label="Activity" value={pr.activity} span2 pre />

                {/* Row 12 */}
                <Field label="SAI No."   value={pr.saiNo} />
                <Field label="ALOBS No." value={pr.alobsNo} />

                {/* Row 13 — Total Amount highlighted */}
                <div className="md:col-span-2 flex justify-end">
                  <div className="text-right">
                    <p className="text-xs font-medium text-slate-500 mb-1">Total Amount</p>
                    <p className="text-xl font-bold text-green-700 tabular-nums">
                      ₱ {fmt(pr.totalAmount)}
                    </p>
                  </div>
                </div>

              </div>
            </div>

            {/* ── Section 2 — Line Items (GS column structure) ─────────────── */}
            {/* Columns: # | Description | Stock No. | Unit | Qty Ordered |     */}
            {/*          Qty Delivered | Qty Distributed | Remaining             */}
            <div className="bg-white border border-slate-200 shadow-sm rounded-lg overflow-hidden">
              <SectionHeading
                number="2"
                title="Line Items — Ordered vs Delivered vs Distributed vs Remaining"
              />
              <div className="overflow-x-auto">
                <table className="w-full text-xs border-collapse">
                  <thead>
                    <tr className="bg-green-800 text-white text-xs uppercase tracking-wide">
                      <th className="px-3 py-2.5 text-center font-medium w-10">#</th>
                      <th className="px-3 py-2.5 text-left font-medium min-w-56">Item Description</th>
                      <th className="px-3 py-2.5 text-left font-medium w-36">Stock No.</th>
                      <th className="px-3 py-2.5 text-center font-medium w-20">Unit</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Qty Ordered</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Qty Delivered</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Qty Distributed</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Remaining</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {pr.items.length === 0 ? (
                      <tr>
                        <td colSpan={8} className="text-center py-10 text-slate-400 text-sm">
                          No line items on this PR.
                        </td>
                      </tr>
                    ) : pr.items.map((item, i) => {
                      const delivered    = qtyDelivered[item.itemNo]    ?? 0;
                      const distributed  = qtyDistributed[item.itemNo]  ?? 0;
                      const remaining    = Math.max(0, item.quantity - delivered);
                      const isFull       = remaining === 0 && delivered > 0;

                      return (
                        <tr
                          key={item.id}
                          className={i % 2 === 1 ? "bg-slate-50" : "bg-white"}
                        >
                          <td className="px-3 py-2.5 text-center text-slate-500 font-medium">{item.itemNo}</td>
                          <td className="px-3 py-2.5 text-slate-800 font-bold">{item.description}</td>
                          <td className="px-3 py-2.5 font-mono text-xs text-slate-600">{item.stockNo ?? "—"}</td>
                          <td className="px-3 py-2.5 text-center text-slate-600">{item.unit}</td>
                          {/* Qty Ordered — bold */}
                          <td className="px-3 py-2.5 text-right tabular-nums font-bold text-slate-800">
                            {fmt(item.quantity)}
                          </td>
                          {/* Qty Delivered — blue */}
                          <td className="px-3 py-2.5 text-right tabular-nums font-semibold text-blue-600">
                            {delivered > 0 ? fmt(delivered) : <span className="text-slate-300">0</span>}
                          </td>
                          {/* Qty Distributed — amber */}
                          <td className="px-3 py-2.5 text-right tabular-nums font-semibold text-amber-600">
                            {distributed > 0 ? fmt(distributed) : <span className="text-slate-300">0</span>}
                          </td>
                          {/* Remaining — green if > 0, red if fully delivered */}
                          <td className={`px-3 py-2.5 text-right tabular-nums font-bold ${
                            isFull
                              ? "text-red-500 bg-red-50"
                              : remaining > 0
                              ? "text-green-600"
                              : "text-slate-400"
                          }`}>
                            {isFull ? "0" : fmt(remaining)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            </div>

            {/* ── Section 3 — Distribution ─────────────────────────────────── */}
            <div className="bg-white border border-slate-200 shadow-sm rounded-lg overflow-hidden">
              <SectionHeading number="3" title="Distribution" />

              {report!.distributions.length === 0 ? (
                <div className="flex flex-col items-center justify-center py-14 gap-2 text-slate-400">
                  <span className="text-3xl">📭</span>
                  <p className="text-sm">No deliveries recorded yet for this PR.</p>
                </div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-xs border-collapse">
                    <thead>
                      <tr className="bg-green-800 text-white text-xs uppercase tracking-wide">
                        <th className="px-3 py-2.5 text-center font-medium w-10">Item#</th>
                        <th className="px-3 py-2.5 text-left font-medium min-w-44">Description</th>
                        <th className="px-3 py-2.5 text-left font-medium w-20">Unit</th>
                        <th className="px-3 py-2.5 text-right font-medium w-24">Qty Delivered</th>
                        <th className="px-3 py-2.5 text-left font-medium w-40">Delivery Ref</th>
                        <th className="px-3 py-2.5 text-left font-medium w-28">Del. Date</th>
                        <th className="px-3 py-2.5 text-left font-medium w-24">Division</th>
                        <th className="px-3 py-2.5 text-right font-medium w-24">Qty Issued</th>
                        <th className="px-3 py-2.5 text-left font-medium w-40">Issue Ref</th>
                        <th className="px-3 py-2.5 text-left font-medium w-28">Date Issued</th>
                        <th className="px-3 py-2.5 text-left font-medium w-36">Issued By</th>
                        <th className="px-3 py-2.5 text-left font-medium w-36">Remarks</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {report!.distributions.map((dist, i) => (
                        <tr
                          key={`${dist.issueRef}-${i}`}
                          className={i % 2 === 1 ? "bg-slate-50" : "bg-white"}
                        >
                          <td className="px-3 py-2 text-center text-slate-400">{dist.itemNo}</td>
                          <td className="px-3 py-2 text-slate-800 max-w-xs">
                            <span className="truncate block font-bold" title={dist.description}>{dist.description}</span>
                          </td>
                          <td className="px-3 py-2 text-slate-600">{dist.unit}</td>
                          <td className="px-3 py-2 text-right tabular-nums text-slate-700">{fmt(dist.qtyDelivered)}</td>
                          <td className="px-3 py-2 font-mono text-slate-600">{dist.deliveryRef}</td>
                          <td className="px-3 py-2 text-slate-600">{fmtDate(dist.deliveryDate)}</td>
                          <td className="px-3 py-2">
                            <span className="px-1.5 py-0.5 rounded bg-green-50 text-green-700 text-xs font-medium border border-green-200">
                              {dist.division}
                            </span>
                          </td>
                          <td className="px-3 py-2 text-right tabular-nums font-bold text-slate-800">{fmt(dist.qtyIssued)}</td>
                          <td className="px-3 py-2 font-mono text-slate-500">{dist.issueRef}</td>
                          <td className="px-3 py-2 text-slate-600">{fmtDate(dist.dateIssued)}</td>
                          <td className="px-3 py-2 text-slate-700">{dist.issuedBy}</td>
                          <td className="px-3 py-2 text-slate-500">{dist.remarks ?? "—"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>

                  {/* Section 3 footer */}
                  <div className="px-4 py-2 border-t border-slate-100 flex items-center justify-between text-xs text-slate-400">
                    <span>{report!.distributions.length} distribution row{report!.distributions.length !== 1 ? "s" : ""}</span>
                    <span className="font-semibold text-slate-600 tabular-nums">
                      Total Issued: {fmt(report!.distributions.reduce((s, d) => s + d.qtyIssued, 0))}
                    </span>
                  </div>
                </div>
              )}
            </div>

          </>
        )}
      </div>
    </div>
  );
}
