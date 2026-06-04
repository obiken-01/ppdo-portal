"use client";

/**
 * Distribution page.
 *
 * Flow:
 *   1. Search / select an item by StockNo or description
 *   2. View item summary (delivered, distributed, on hand)
 *   3. See delivery breakdown — each batch showing available qty
 *   4. Click "Distribute" on a batch → fill in qty, recipient, division, date
 *   5. Submit → creates a Distribution record
 *
 * Filters (in filter panel):
 *   - Division  — filter breakdown to a specific receiving division
 *   - Date range — filter existing distributions by date issued
 *
 * API:
 *   GET  /api/items/lookup?term=…                   → autocomplete
 *   GET  /api/distributions/item/{stockNo}           → item summary + breakdown
 *   POST /api/distributions                          → create distribution
 */

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { useToast } from "@/components/ui/Toast";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import type {
  CreateDistributionStandaloneRequest,
  DeliveryItemBreakdownResponse,
  DistributionCreatedResponse,
  ItemDistributionSummaryResponse,
  ItemLookupResponse,
  MeResponse,
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
// Sub-components
// ---------------------------------------------------------------------------

function SectionHeading({ title }: { title: string }) {
  return (
    <div className="px-5 py-3 bg-green-600 text-white text-sm font-semibold uppercase tracking-wide">
      {title}
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
// Distribution Form (shown inline per delivery batch)
// ---------------------------------------------------------------------------

interface DistFormState {
  deliveryItemId: string;
  qty:            string;
  dateIssued:     string;
  issuedBy:       string;
  division:       string;
  remarks:        string;
}

function DistributeForm({
  batch, onSubmit, onCancel, submitting,
}: {
  batch:      DeliveryItemBreakdownResponse;
  onSubmit:   (form: DistFormState) => void;
  onCancel:   () => void;
  submitting: boolean;
}) {
  const [form, setForm] = useState<DistFormState>({
    deliveryItemId: batch.deliveryItemId,
    qty:            "",
    dateIssued:     TODAY,
    issuedBy:       "",
    division:       "",
    remarks:        "",
  });

  function patch<K extends keyof DistFormState>(key: K, val: DistFormState[K]) {
    setForm((f) => ({ ...f, [key]: val }));
  }

  const qtyNum    = parseFloat(form.qty) || 0;
  const overLimit = qtyNum > batch.qtyAvailable;
  const canSubmit = qtyNum > 0 && !overLimit && form.issuedBy.trim() && form.division;

  return (
    <div className="bg-green-50 border border-green-200 p-4 space-y-3">
      <p className="text-xs font-semibold text-green-700 uppercase tracking-wide">
        Distribute from {batch.deliveryRef} — {batch.qtyAvailable} {batch.qtyAvailable === 1 ? "unit" : "units"} available
      </p>
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        {/* Qty */}
        <div className="space-y-1">
          <label className="text-xs font-medium text-slate-500">Qty to Distribute *</label>
          <input
            type="number" min={0.01} step="any" max={batch.qtyAvailable}
            value={form.qty}
            onChange={(e) => patch("qty", e.target.value)}
            placeholder={`Max ${batch.qtyAvailable}`}
            className={`w-full px-2.5 py-1.5 text-sm border focus:outline-none focus:ring-1 focus:ring-green-500 bg-white ${
              overLimit ? "border-red-400" : "border-slate-200"
            }`}
          />
          {overLimit && <p className="text-xs text-red-500">Exceeds available ({batch.qtyAvailable})</p>}
        </div>

        {/* Date */}
        <div className="space-y-1">
          <label className="text-xs font-medium text-slate-500">Date Issued *</label>
          <input
            type="date" value={form.dateIssued}
            onChange={(e) => patch("dateIssued", e.target.value)}
            className="w-full px-2.5 py-1.5 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
          />
        </div>

        {/* Issued To */}
        <div className="space-y-1">
          <label className="text-xs font-medium text-slate-500">Issued To (Name) *</label>
          <input
            type="text" value={form.issuedBy}
            onChange={(e) => patch("issuedBy", e.target.value)}
            placeholder="Recipient name"
            className="w-full px-2.5 py-1.5 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
          />
        </div>

        {/* Division */}
        <div className="space-y-1">
          <label className="text-xs font-medium text-slate-500">Division *</label>
          <select
            value={form.division}
            onChange={(e) => patch("division", e.target.value)}
            className="w-full px-2.5 py-1.5 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
          >
            <option value="">— Select —</option>
            {DIVISIONS.map((d) => <option key={d} value={d}>{d}</option>)}
          </select>
        </div>

        {/* Remarks — full width */}
        <div className="col-span-2 md:col-span-4 space-y-1">
          <label className="text-xs font-medium text-slate-500">Remarks (optional)</label>
          <input
            type="text" value={form.remarks}
            onChange={(e) => patch("remarks", e.target.value)}
            placeholder="Optional notes"
            className="w-full px-2.5 py-1.5 text-sm border border-slate-200 bg-white focus:outline-none focus:ring-1 focus:ring-green-500"
          />
        </div>
      </div>

      <div className="flex items-center gap-2 pt-1">
        <button
          onClick={() => onSubmit(form)}
          disabled={!canSubmit || submitting}
          className="flex items-center gap-1.5 px-4 py-2 text-sm bg-green-600 text-white font-medium hover:bg-green-500 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {submitting
            ? <span className="w-3 h-3 border-2 border-white border-t-transparent rounded-full animate-spin" />
            : "✓"}
          {submitting ? "Saving…" : "Confirm Distribution"}
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

  const [authChecked, setAuthChecked] = useState(false);

  // Item search
  const [searchTerm, setSearchTerm]   = useState("");
  const [suggestions, setSuggestions] = useState<ItemLookupResponse[]>([]);
  const [suggestOpen, setSuggestOpen] = useState(false);

  // Selected item + its breakdown
  const [selectedStockNo, setSelectedStockNo]   = useState("");
  const [summary, setSummary]                   = useState<ItemDistributionSummaryResponse | null>(null);
  const [summaryLoading, setSummaryLoading]     = useState(false);

  // Which batch has the distribute form open
  const [activeDeliveryItemId, setActiveDeliveryItemId] = useState<string | null>(null);
  const [submitting, setSubmitting]                     = useState(false);

  // Confirm dialog
  const [dialog, setDialog] = useState<ConfirmDialogProps | null>(null);

  // Filters on the breakdown / history view
  const [filterDivision, setFilterDivision] = useState("");
  const [filterDateFrom, setFilterDateFrom] = useState("");
  const [filterDateTo,   setFilterDateTo]   = useState("");
  const [filtersOpen,    setFiltersOpen]    = useState(false);

  // Auth
  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory) { router.replace("/dashboard"); return; }
        setAuthChecked(true);
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
    setActiveDeliveryItemId(null);
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

  // Submit distribution
  async function handleDistribute(form: DistFormState) {
    setSubmitting(true);
    try {
      const payload: CreateDistributionStandaloneRequest = {
        deliveryItemId: form.deliveryItemId,
        division:       form.division,
        qtyIssued:      parseFloat(form.qty),
        dateIssued:     form.dateIssued,
        issuedBy:       form.issuedBy.trim(),
        remarks:        form.remarks.trim() || null,
      };
      await api.post<DistributionCreatedResponse>("/distributions", payload);
      toast.success("Distribution recorded", `${payload.qtyIssued} units issued to ${payload.division}.`);
      setActiveDeliveryItemId(null);
      // Reload breakdown to show updated qtys
      if (selectedStockNo && summary) {
        await loadSummary(selectedStockNo, summary.description);
      }
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: string } })?.response?.data
        ?? "Could not record distribution. Please try again.";
      toast.error("Failed", msg);
    } finally { setSubmitting(false); }
  }

  // Filter existing distributions across all batches
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
              onClick={() => { setSelectedStockNo(""); setSummary(null); setSearchTerm(""); }}
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

        {/* ── Item summary card ─────────────────────────────────────────────── */}
        {summary && !summaryLoading && (
          <>
            <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
              <SectionHeading title={`${summary.stockNo} — ${summary.description}`} />
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
            </div>

            {/* ── Delivery breakdown ──────────────────────────────────────────── */}
            <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
              <SectionHeading title="Delivery Batches" />
              {summary.deliveryItems.length === 0 ? (
                <div className="text-center py-12 text-slate-400 text-sm">No delivery batches found.</div>
              ) : (
                <div className="divide-y divide-slate-100">
                  {summary.deliveryItems.map((batch) => {
                    const isActive = activeDeliveryItemId === batch.deliveryItemId;
                    return (
                      <div key={batch.deliveryItemId}>
                        {/* Batch header row */}
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
                              <p className="text-xs text-slate-400">Available</p>
                              <p className={`font-bold tabular-nums ${
                                batch.qtyAvailable > 0 ? "text-green-700" : "text-slate-400"
                              }`}>
                                {batch.qtyAvailable > 0 ? fmt(batch.qtyAvailable) : "None"}
                              </p>
                            </div>
                          </div>
                          {batch.qtyAvailable > 0 && (
                            <button
                              onClick={() => setActiveDeliveryItemId(isActive ? null : batch.deliveryItemId)}
                              className={`px-4 py-2 text-xs font-medium border transition-colors shrink-0 ${
                                isActive
                                  ? "bg-slate-100 border-slate-300 text-slate-600"
                                  : "bg-green-50 border-green-300 text-green-700 hover:bg-green-100"
                              }`}
                            >
                              {isActive ? "Cancel" : "Distribute"}
                            </button>
                          )}
                        </div>

                        {/* Distribution form */}
                        {isActive && (
                          <div className="px-5 pb-4">
                            <DistributeForm
                              batch={batch}
                              onSubmit={handleDistribute}
                              onCancel={() => setActiveDeliveryItemId(null)}
                              submitting={submitting}
                            />
                          </div>
                        )}

                        {/* Existing distributions for this batch */}
                        {batch.distributions.length > 0 && (
                          <div className="px-5 pb-3">
                            <p className="text-xs font-medium text-slate-400 mb-2 uppercase tracking-wide">
                              Distributions from this batch
                            </p>
                            <table className="w-full text-xs border-collapse">
                              <thead>
                                <tr className="bg-slate-50 text-slate-500 border-b border-slate-200">
                                  <th className="text-left px-2 py-1.5 font-medium">Issue Ref</th>
                                  <th className="text-left px-2 py-1.5 font-medium">Division</th>
                                  <th className="text-right px-2 py-1.5 font-medium">Qty Issued</th>
                                  <th className="text-left px-2 py-1.5 font-medium">Date Issued</th>
                                  <th className="text-left px-2 py-1.5 font-medium">Issued To</th>
                                  <th className="text-left px-2 py-1.5 font-medium">Remarks</th>
                                </tr>
                              </thead>
                              <tbody className="divide-y divide-slate-50">
                                {batch.distributions.map((d) => (
                                  <tr key={d.id} className="hover:bg-slate-50">
                                    <td className="px-2 py-1.5 font-mono text-slate-600">{d.issueRef}</td>
                                    <td className="px-2 py-1.5">
                                      <span className="px-1.5 py-0.5 bg-green-50 text-green-700 border border-green-200 text-xs">
                                        {d.division}
                                      </span>
                                    </td>
                                    <td className="px-2 py-1.5 text-right tabular-nums font-semibold text-slate-800">
                                      {fmt(d.qtyIssued)}
                                    </td>
                                    <td className="px-2 py-1.5 text-slate-600">{fmtDate(d.dateIssued)}</td>
                                    <td className="px-2 py-1.5 text-slate-700">{d.issuedBy}</td>
                                    <td className="px-2 py-1.5 text-slate-400">{d.remarks ?? "—"}</td>
                                  </tr>
                                ))}
                              </tbody>
                            </table>
                          </div>
                        )}
                      </div>
                    );
                  })}
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

              {/* Filter panel */}
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
                <div className="overflow-x-auto">
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

      {dialog && <ConfirmDialog {...dialog} />}
    </div>
  );
}
