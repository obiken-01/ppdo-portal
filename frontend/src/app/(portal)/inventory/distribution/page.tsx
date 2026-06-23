"use client";

/**
 * Distribution page.
 *
 * Flow:
 *   1. Search / select an item by StockNo or description
 *   2. View item summary (delivered, distributed, on hand) + single "Distribute" button
 *   3. See delivery batches as read-only stock sources
 *   4. Click "Distribute" → fill in qty, recipient, division, date
 *   5. Submit → allocates FIFO across available batches → creates Distribution records
 *
 * API:
 *   GET  /api/items/lookup?term=…                   → autocomplete
 *   GET  /api/distributions/item/{stockNo}           → item summary + breakdown
 *   POST /api/distributions                          → create distribution (per batch)
 */

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { fetchMe } from "@/lib/me-cache";
import { useToast } from "@/components/ui/Toast";
import type {
  CreateDistributionStandaloneRequest,
  DeliveryItemBreakdownResponse,
  DistributionCreatedResponse,
  ItemDistributionSummaryResponse,
  ItemLookupResponse,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DIVISIONS = ["Admin", "Planning", "RM", "MIS", "SPD"];
const TODAY     = new Date().toISOString().slice(0, 10);

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 0, maximumFractionDigits: 2,
  }).format(n);
}

function fmtDate(d: string) {
  if (!d) return "—";
  return new Date(d).toLocaleDateString("en-PH", {
    year: "numeric", month: "short", day: "numeric",
  });
}

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function SectionHeading({ title, action }: { title: string; action?: React.ReactNode }) {
  return (
    <div className="px-5 py-3 bg-green-600 text-white text-sm font-semibold uppercase tracking-wide flex items-center justify-between">
      <span>{title}</span>
      {action}
    </div>
  );
}

function StatCell({ label, value, accent }: { label: string; value: string; accent?: string }) {
  return (
    <div className="text-center px-4 py-3 border-r border-slate-100 last:border-0">
      <p className="text-xs text-slate-500 mb-0.5">{label}</p>
      <p className={`text-lg font-bold tabular-nums ${accent ?? "text-slate-800"}`}>{value}</p>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Distribution Form — item-level, FIFO allocation across batches on submit
// ---------------------------------------------------------------------------

interface SplitRow {
  _id:        string;
  division:   string;
  qty:        string;
  issuedBy:   string;
  dateIssued: string;
  remarks:    string;
}

function blankRow(date: string): SplitRow {
  return { _id: `${Date.now()}-${Math.random()}`, division: "", qty: "", issuedBy: "", dateIssued: date, remarks: "" };
}

function DistributeForm({
  totalAvailable, unit, onSubmit, onCancel, submitting,
}: {
  totalAvailable: number;
  unit:           string;
  onSubmit:       (rows: SplitRow[]) => void;
  onCancel:       () => void;
  submitting:     boolean;
}) {
  const [rows, setRows] = useState<SplitRow[]>([blankRow(TODAY)]);

  function patchRow(id: string, patch: Partial<SplitRow>) {
    setRows((prev) => prev.map((r) => r._id === id ? { ...r, ...patch } : r));
  }

  function addRow() {
    setRows((prev) => [...prev, blankRow(rows[0]?.dateIssued ?? TODAY)]);
  }

  function removeRow(id: string) {
    setRows((prev) => prev.filter((r) => r._id !== id));
  }

  const totalQty   = rows.reduce((s, r) => s + (parseFloat(r.qty) || 0), 0);
  const overLimit  = totalQty > totalAvailable;
  const hasInvalid = rows.some((r) => !r.division || !r.issuedBy.trim() || !(parseFloat(r.qty) > 0));
  const canSubmit  = rows.length > 0 && !overLimit && !hasInvalid && totalAvailable > 0;

  return (
    <div className="px-5 pb-5 pt-4 space-y-3 border-t border-slate-100">
      <div className="flex items-center justify-between">
        <p className="text-xs font-semibold text-slate-600 uppercase tracking-wide">
          Distribute — {fmt(totalAvailable)} {unit} on hand
        </p>
        <button
          onClick={addRow}
          className="flex items-center gap-1 px-2.5 py-1 text-xs border border-green-400 text-green-700 hover:bg-green-50 transition-colors"
        >
          + Add Division
        </button>
      </div>

      {/* Column headers */}
      <div className="grid grid-cols-12 gap-2 text-xs font-medium text-slate-500 px-1">
        <span className="col-span-2">Division *</span>
        <span className="col-span-2">Qty *</span>
        <span className="col-span-3">Issued To (Name) *</span>
        <span className="col-span-2">Date Issued *</span>
        <span className="col-span-2">Remarks</span>
        <span className="col-span-1" />
      </div>

      {/* Split rows */}
      <div className="space-y-2">
        {rows.map((row) => {
          const rowQty = parseFloat(row.qty) || 0;
          void rowQty;
          return (
            <div key={row._id} className="grid grid-cols-12 gap-2 items-start">
              <div className="col-span-2">
                <select
                  value={row.division}
                  onChange={(e) => patchRow(row._id, { division: e.target.value })}
                  className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                >
                  <option value="">— Select —</option>
                  {DIVISIONS.map((d) => <option key={d} value={d}>{d}</option>)}
                </select>
              </div>
              <div className="col-span-2">
                <input
                  type="number" min={0.01} step="any"
                  value={row.qty}
                  onChange={(e) => patchRow(row._id, { qty: e.target.value })}
                  placeholder="0"
                  className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-white text-right focus:outline-none focus:ring-1 focus:ring-green-500"
                />
              </div>
              <div className="col-span-3">
                <input
                  type="text"
                  value={row.issuedBy}
                  onChange={(e) => patchRow(row._id, { issuedBy: e.target.value })}
                  placeholder="Recipient name"
                  className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                />
              </div>
              <div className="col-span-2">
                <input
                  type="date"
                  value={row.dateIssued}
                  onChange={(e) => patchRow(row._id, { dateIssued: e.target.value })}
                  className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                />
              </div>
              <div className="col-span-2">
                <input
                  type="text"
                  value={row.remarks}
                  onChange={(e) => patchRow(row._id, { remarks: e.target.value })}
                  placeholder="Optional"
                  className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                />
              </div>
              <div className="col-span-1 flex justify-center pt-1.5">
                <button
                  onClick={() => removeRow(row._id)}
                  disabled={rows.length === 1}
                  className="text-slate-300 hover:text-red-400 disabled:opacity-20 transition-colors text-sm"
                  title="Remove row"
                >✕</button>
              </div>
            </div>
          );
        })}
      </div>

      {/* Total / validation */}
      <div className="text-xs pt-1">
        <span className={`font-semibold tabular-nums ${overLimit ? "text-red-500" : "text-slate-700"}`}>
          Total: {fmt(totalQty)} / {fmt(totalAvailable)} on hand
          {overLimit && " — exceeds available stock"}
        </span>
      </div>

      {/* Actions */}
      <div className="flex items-center gap-2 border-t border-slate-100 pt-3">
        <button
          onClick={() => onSubmit(rows)}
          disabled={!canSubmit || submitting}
          className="flex items-center gap-1.5 px-4 py-2 text-sm bg-green-600 text-white font-medium hover:bg-green-500 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {submitting
            ? <span className="w-3 h-3 border-2 border-white border-t-transparent rounded-full animate-spin" />
            : "✓"}
          {submitting ? "Saving…" : rows.length > 1 ? `Confirm ${rows.length} Distributions` : "Confirm Distribution"}
        </button>
        <button
          onClick={onCancel}
          className="px-4 py-2 text-sm border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function DistributionPage() {
  const router    = useRouter();
  const { toast } = useToast();

  const [authChecked] = useState(true);

  // Item search
  const [searchTerm, setSearchTerm]   = useState("");
  const [suggestions, setSuggestions] = useState<ItemLookupResponse[]>([]);
  const [suggestOpen, setSuggestOpen] = useState(false);

  // Selected item + its breakdown
  const [selectedStockNo, setSelectedStockNo]   = useState("");
  const [summary, setSummary]                   = useState<ItemDistributionSummaryResponse | null>(null);
  const [summaryLoading, setSummaryLoading]     = useState(false);

  // Whether the item-level distribute form is open
  const [formOpen,    setFormOpen]    = useState(false);
  const [submitting,  setSubmitting]  = useState(false);


  // Filters on history view
  const [filterDivision, setFilterDivision] = useState("");
  const [filterDateFrom, setFilterDateFrom] = useState("");
  const [filterDateTo,   setFilterDateTo]   = useState("");
  const [filtersOpen,    setFiltersOpen]    = useState(false);

  // Auth
  useEffect(() => {
    fetchMe()
      .then((data) => {
        if (!data.canAccessInventory) router.replace(data.officeId != null ? "/budget-planning" : "/dashboard");
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // Item autocomplete — debounced
  useEffect(() => {
    if (!searchTerm.trim() || searchTerm === summary?.description) {
      setSuggestions([]); return;
    }
    const id = setTimeout(async () => {
      try {
        const { data } = await api.get<ItemLookupResponse[]>(
          `/items/lookup?term=${encodeURIComponent(searchTerm)}`
        );
        setSuggestions(data);
        setSuggestOpen(true);
      } catch { /* silent */ }
    }, 250);
    return () => clearTimeout(id);
  }, [searchTerm, summary?.description]);

  // Load item breakdown
  async function loadSummary(stockNo: string, description: string) {
    setSelectedStockNo(stockNo);
    setSearchTerm(description);
    setSuggestOpen(false);
    setSummary(null);
    setFormOpen(false);
    setSummaryLoading(true);
    try {
      const { data } = await api.get<ItemDistributionSummaryResponse>(
        `/distributions/item/${encodeURIComponent(stockNo)}`
      );
      setSummary(data);
    } catch (e: unknown) {
      const status = (e as { response?: { status?: number } })?.response?.status;
      if (status === 404) {
        toast.error("No data", `No delivery activity found for ${stockNo}.`);
      } else {
        toast.error("Load failed", "Could not load item breakdown.");
      }
    } finally { setSummaryLoading(false); }
  }

  /**
   * FIFO allocation across available delivery batches.
   * Each SplitRow may consume from multiple batches; we create one API call per
   * (row × batch) pair so the backend still records which delivery batch was used.
   */
  async function handleDistribute(rows: SplitRow[]) {
    if (!summary) return;
    setSubmitting(true);

    // Build an ordered list of batches with remaining available qty (earliest first)
    const batchPool: { batch: DeliveryItemBreakdownResponse; remaining: number }[] =
      summary.deliveryItems
        .filter((b) => b.qtyAvailable > 0)
        .map((b) => ({ batch: b, remaining: b.qtyAvailable }));

    try {
      for (const row of rows) {
        let need = parseFloat(row.qty);
        for (const slot of batchPool) {
          if (need <= 0) break;
          if (slot.remaining <= 0) continue;

          const take = Math.min(need, slot.remaining);
          const payload: CreateDistributionStandaloneRequest = {
            deliveryItemId: slot.batch.deliveryItemId,
            division:       row.division,
            qtyIssued:      take,
            dateIssued:     row.dateIssued,
            issuedBy:       row.issuedBy.trim(),
            remarks:        row.remarks.trim() || null,
          };
          await api.post<DistributionCreatedResponse>("/distributions", payload);
          slot.remaining -= take;
          need           -= take;
        }
      }

      const totalQty = rows.reduce((s, r) => s + (parseFloat(r.qty) || 0), 0);
      const divLabel = rows.length === 1
        ? rows[0].division
        : `${rows.length} divisions`;
      toast.success("Distribution recorded", `${fmt(totalQty)} units issued to ${divLabel}.`);
      setFormOpen(false);
      await loadSummary(selectedStockNo, summary.description);
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: string } })?.response?.data
        ?? "Could not record distribution. Please try again.";
      toast.error("Failed", msg);
    } finally { setSubmitting(false); }
  }

  // Filter distribution history
  const filteredDistributions = useMemo(() => {
    if (!summary) return [];
    const all = summary.deliveryItems.flatMap((b) =>
      b.distributions.map((d) => ({ ...d, deliveryRef: b.deliveryRef, prNo: b.prNo }))
    );
    return all.filter((d) => {
      if (filterDivision && d.division !== filterDivision) return false;
      if (filterDateFrom && d.dateIssued < filterDateFrom) return false;
      if (filterDateTo   && d.dateIssued > filterDateTo)   return false;
      return true;
    });
  }, [summary, filterDivision, filterDateFrom, filterDateTo]);

  const filterCount =
    (filterDivision ? 1 : 0) + (filterDateFrom ? 1 : 0) + (filterDateTo ? 1 : 0);

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-4">

        {/* ── Item search ───────────────────────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm p-5 space-y-3">
          <p className="text-sm font-semibold text-slate-700">Select an Item</p>
          <div className="relative">
            <input
              value={searchTerm}
              onChange={(e) => { setSearchTerm(e.target.value); setSuggestOpen(true); }}
              onFocus={() => suggestions.length > 0 && setSuggestOpen(true)}
              onBlur={() => setTimeout(() => setSuggestOpen(false), 150)}
              placeholder="Type stock no. or description…"
              className="w-full px-4 py-2.5 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-2 focus:ring-green-600"
            />
            {suggestOpen && suggestions.length > 0 && (
              <ul className="absolute z-50 top-full left-0 right-0 bg-white border border-slate-200 shadow-xl max-h-60 overflow-y-auto text-sm">
                {suggestions.map((item) => (
                  <li
                    key={item.id}
                    onMouseDown={(e) => { e.preventDefault(); loadSummary(item.stockNo, item.description); }}
                    className="px-4 py-2.5 cursor-pointer hover:bg-green-50 border-b border-slate-50 last:border-0"
                  >
                    <div className="flex items-center gap-2">
                      <span className="font-mono text-xs text-slate-600">{item.stockNo}</span>
                      <span className="text-slate-800">{item.description}</span>
                      <span className="text-xs text-slate-400 ml-auto">{item.unit}</span>
                    </div>
                  </li>
                ))}
              </ul>
            )}
          </div>
          {selectedStockNo && summary && (
            <button
              onClick={() => { setSelectedStockNo(""); setSummary(null); setSearchTerm(""); setFormOpen(false); }}
              className="text-xs text-slate-400 hover:text-slate-600"
            >
              ✕ Clear selection
            </button>
          )}
        </div>

        {/* ── Loading ───────────────────────────────────────────────────────── */}
        {summaryLoading && (
          <div className="bg-white border border-slate-200 shadow-sm flex items-center justify-center py-16">
            <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
          </div>
        )}

        {summary && !summaryLoading && (
          <>
            {/* ── Item summary card + Distribute button ─────────────────────── */}
            <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
              <div className="px-5 py-3 bg-green-600 text-white flex items-center justify-between">
                <span className="text-sm font-semibold uppercase tracking-wide">
                  {summary.stockNo} — {summary.description}
                </span>
                {!formOpen && (
                  <button
                    onClick={() => setFormOpen(true)}
                    disabled={summary.onHand <= 0}
                    className="px-4 py-1.5 text-xs font-semibold bg-white text-green-700 hover:bg-green-50 transition-colors disabled:opacity-40 disabled:cursor-not-allowed"
                  >
                    Distribute
                  </button>
                )}
                {formOpen && (
                  <button
                    onClick={() => setFormOpen(false)}
                    className="px-4 py-1.5 text-xs font-semibold bg-green-500 text-white hover:bg-green-400 border border-green-400 transition-colors"
                  >
                    Cancel
                  </button>
                )}
              </div>

              {/* Stats */}
              <div className="grid grid-cols-2 md:grid-cols-4 divide-x divide-slate-100">
                <StatCell label="Unit" value={summary.unit} />
                <StatCell label="Total Delivered" value={fmt(summary.totalDelivered)} />
                <StatCell label="Total Distributed" value={fmt(summary.totalDistributed)} />
                <StatCell
                  label="On Hand (Available)"
                  value={fmt(summary.onHand)}
                  accent={summary.onHand > 0 ? "text-green-700" : "text-red-500"}
                />
              </div>

              {/* Inline distribute form */}
              {formOpen && (
                <DistributeForm
                  totalAvailable={summary.onHand}
                  unit={summary.unit}
                  onSubmit={(rows) => void handleDistribute(rows)}
                  onCancel={() => setFormOpen(false)}
                  submitting={submitting}
                />
              )}
            </div>

            {/* ── Stock sources (delivery batches — read only) ───────────────── */}
            <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
              <SectionHeading title="Stock Sources" />
              {summary.deliveryItems.length === 0 ? (
                <div className="text-center py-12 text-slate-400 text-sm">No delivery batches found.</div>
              ) : (
                <div className="divide-y divide-slate-100">
                  {summary.deliveryItems.map((batch) => (
                    <div key={batch.deliveryItemId}>
                      {/* Batch row */}
                      <div className="flex items-center gap-4 px-5 py-3 flex-wrap">
                        <div className="flex-1 min-w-0 grid grid-cols-2 md:grid-cols-5 gap-3 text-sm">
                          <div>
                            <p className="text-xs text-slate-400">Delivery Ref</p>
                            <p className="font-mono font-semibold text-slate-800">{batch.deliveryRef}</p>
                          </div>
                          <div>
                            <p className="text-xs text-slate-400">PR No.</p>
                            <p className="font-mono text-xs text-slate-600">{batch.prNo}</p>
                          </div>
                          <div>
                            <p className="text-xs text-slate-400">Date</p>
                            <p className="text-slate-700">{fmtDate(batch.deliveryDate)}</p>
                          </div>
                          <div>
                            <p className="text-xs text-slate-400">Delivered / Distributed</p>
                            <p className="tabular-nums text-slate-700">
                              {fmt(batch.qtyDelivered)} / {fmt(batch.qtyDistributed)}
                            </p>
                          </div>
                          <div>
                            <p className="text-xs text-slate-400">Available from batch</p>
                            <p className={`font-bold tabular-nums ${
                              batch.qtyAvailable > 0 ? "text-green-700" : "text-slate-400"
                            }`}>
                              {batch.qtyAvailable > 0 ? fmt(batch.qtyAvailable) : "None"}
                            </p>
                          </div>
                        </div>
                      </div>

                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* ── All distributions — with filters ──────────────────────────── */}
            <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
              <div className="flex items-center justify-between px-5 py-3 bg-green-600 text-white">
                <span className="text-sm font-semibold uppercase tracking-wide">
                  All Distributions for {summary.stockNo}
                </span>
                <button
                  onClick={() => setFiltersOpen((o) => !o)}
                  className={`flex items-center gap-1.5 px-3 py-1 text-xs border transition-colors ${
                    filtersOpen || filterCount > 0
                      ? "bg-white text-green-700 border-white"
                      : "border-green-400 text-green-100 hover:bg-green-500"
                  }`}
                >
                  ⚙ Filters
                  {filterCount > 0 && (
                    <span className="bg-white text-green-700 rounded-full w-4 h-4 flex items-center justify-center text-xs font-bold">
                      {filterCount}
                    </span>
                  )}
                </button>
              </div>

              {filtersOpen && (
                <div className="px-5 py-4 border-b border-slate-100 bg-slate-50 flex flex-wrap gap-4">
                  <div className="space-y-1">
                    <label className="text-xs font-medium text-slate-500">Division</label>
                    <select
                      value={filterDivision}
                      onChange={(e) => setFilterDivision(e.target.value)}
                      className="px-2.5 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                    >
                      <option value="">All divisions</option>
                      {DIVISIONS.map((d) => <option key={d} value={d}>{d}</option>)}
                    </select>
                  </div>
                  <div className="space-y-1">
                    <label className="text-xs font-medium text-slate-500">Date Issued — From</label>
                    <input
                      type="date" value={filterDateFrom}
                      onChange={(e) => setFilterDateFrom(e.target.value)}
                      className="px-2.5 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                    />
                  </div>
                  <div className="space-y-1">
                    <label className="text-xs font-medium text-slate-500">To</label>
                    <input
                      type="date" value={filterDateTo}
                      onChange={(e) => setFilterDateTo(e.target.value)}
                      className="px-2.5 py-1.5 text-xs border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
                    />
                  </div>
                  {filterCount > 0 && (
                    <div className="flex items-end">
                      <button
                        onClick={() => { setFilterDivision(""); setFilterDateFrom(""); setFilterDateTo(""); }}
                        className="px-3 py-1.5 text-xs border border-slate-200 text-slate-500 hover:bg-white transition-colors"
                      >
                        ✕ Clear filters
                      </button>
                    </div>
                  )}
                </div>
              )}

              {filteredDistributions.length === 0 ? (
                <div className="text-center py-10 text-slate-400 text-sm">
                  {summary.deliveryItems.flatMap((b) => b.distributions).length === 0
                    ? "No distributions recorded yet for this item."
                    : "No distributions match your filters."}
                </div>
              ) : (
                <div className="overflow-x-auto overflow-y-hidden">
                  <table className="w-full text-sm border-collapse">
                    <thead>
                      <tr className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 uppercase tracking-wide">
                        <th className="text-left px-4 py-2.5 font-medium">Issue Ref</th>
                        <th className="text-left px-4 py-2.5 font-medium">Delivery Ref</th>
                        <th className="text-left px-4 py-2.5 font-medium">PR No.</th>
                        <th className="text-left px-4 py-2.5 font-medium">Division</th>
                        <th className="text-right px-4 py-2.5 font-medium">Qty Issued</th>
                        <th className="text-left px-4 py-2.5 font-medium">Date Issued</th>
                        <th className="text-left px-4 py-2.5 font-medium">Issued To</th>
                        <th className="text-left px-4 py-2.5 font-medium">Remarks</th>
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-slate-100">
                      {filteredDistributions.map((d, i) => (
                        <tr key={d.id} className={i % 2 === 1 ? "bg-slate-50" : "bg-white"}>
                          <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{d.issueRef}</td>
                          <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{d.deliveryRef}</td>
                          <td className="px-4 py-2.5 font-mono text-xs text-slate-600">{d.prNo}</td>
                          <td className="px-4 py-2.5">
                            <span className="px-2 py-0.5 text-xs bg-green-50 text-green-700 border border-green-200">
                              {d.division}
                            </span>
                          </td>
                          <td className="px-4 py-2.5 text-right tabular-nums font-semibold text-slate-800">
                            {fmt(d.qtyIssued)}
                          </td>
                          <td className="px-4 py-2.5 text-slate-600">{fmtDate(d.dateIssued)}</td>
                          <td className="px-4 py-2.5 text-slate-700">{d.issuedBy}</td>
                          <td className="px-4 py-2.5 text-slate-400">{d.remarks ?? "—"}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                  <div className="px-4 py-2 border-t border-slate-100 text-xs text-slate-400 flex items-center justify-between">
                    <span>{filteredDistributions.length} distribution{filteredDistributions.length !== 1 ? "s" : ""}</span>
                    <span className="font-semibold text-slate-600 tabular-nums">
                      Total Issued: {fmt(filteredDistributions.reduce((s, d) => s + d.qtyIssued, 0))}
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
