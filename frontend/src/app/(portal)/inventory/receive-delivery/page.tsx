"use client";

/**
 * Receive Delivery page — RAL-54 (simplified).
 *
 * Records the physical receipt of goods against a PR.
 * Distribution (who gets the items) is handled separately on the
 * Distribution page after delivery is recorded.
 *
 * API:
 *   GET  /api/purchase-requests          → PR selector
 *   GET  /api/purchase-requests/{id}     → PR items
 *   GET  /api/deliveries?prId={id}       → prior deliveries for remaining calc
 *   POST /api/deliveries                 → submit
 */

import { useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { useToast } from "@/components/ui/Toast";
import type {
  CreateDeliveryItemRequest,
  CreateDeliveryRequest,
  DeliveryResponse,
  DeliverySummaryResponse,
  MeResponse,
  PRResponse,
  PRSummaryResponse,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const TODAY = new Date().toISOString().slice(0, 10);

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(n);
}

// ---------------------------------------------------------------------------
// Client types
// ---------------------------------------------------------------------------

interface ItemRow {
  prItemId:        string;
  itemNo:          number;
  stockNo:         string | null;
  description:     string;
  unit:            string;
  qtyOrdered:      number;
  qtyThisDelivery: string;
}

function itemsFromPR(pr: PRResponse): ItemRow[] {
  return pr.items.map((item) => ({
    prItemId:        item.id,
    itemNo:          item.itemNo,
    stockNo:         item.stockNo,
    description:     item.description,
    unit:            item.unit,
    qtyOrdered:      Number(item.quantity),
    qtyThisDelivery: "",
  }));
}

// ---------------------------------------------------------------------------
// Field components
// ---------------------------------------------------------------------------

function FieldLabel({ children, required }: { children: React.ReactNode; required?: boolean }) {
  return (
    <label className="block text-xs font-medium text-slate-500 mb-1">
      {children}{required && <span className="text-red-500 ml-0.5">*</span>}
    </label>
  );
}

function YellowInput({
  value, onChange, placeholder, type = "text", disabled,
}: {
  value: string; onChange: (v: string) => void;
  placeholder?: string; type?: string; disabled?: boolean;
}) {
  return (
    <input
      type={type} value={value} disabled={disabled}
      onChange={(e) => onChange(e.target.value)}
      placeholder={placeholder}
      className="w-full px-3 py-2 text-sm border border-slate-200 bg-cell-fill focus:outline-none focus:ring-2 focus:ring-green-600 focus:bg-white transition-colors disabled:opacity-60"
    />
  );
}

function GrayInput({ value }: { value: string }) {
  return (
    <input
      readOnly tabIndex={-1} value={value}
      className="w-full px-3 py-2 text-sm border border-slate-200 bg-cell-auto text-slate-500 cursor-default"
    />
  );
}

function SectionHeading({ number, title }: { number: string; title: string }) {
  return (
    <div className="flex items-center gap-3 px-6 py-3 bg-green-600 text-white">
      <span className="w-6 h-6 rounded-full bg-white text-green-700 flex items-center justify-center text-xs font-bold shrink-0">
        {number}
      </span>
      <span className="text-sm font-semibold tracking-wide uppercase">{title}</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// PR Combobox
// ---------------------------------------------------------------------------

const STATUS_BADGE: Record<string, string> = {
  Open:               "bg-blue-100 text-blue-700",
  PartiallyDelivered: "bg-amber-100 text-amber-700",
  FullyDelivered:     "bg-green-100 text-green-700",
};

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
    <div className="relative w-full">
      <div className="flex items-center border border-slate-200 bg-cell-fill focus-within:ring-2 focus-within:ring-green-600">
        <input
          type="text" value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          onFocus={onFocus} onBlur={onBlur}
          placeholder={loading ? "Loading PRs…" : "Type PR number, division, or status…"}
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
            <li className="px-4 py-3 text-slate-400 text-xs">No PRs match.</li>
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
                {pr.id === selectedId && <span className="ml-auto text-green-600 text-xs">✓</span>}
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

export default function ReceiveDeliveryPage() {
  const router    = useRouter();
  const { toast } = useToast();

  const [me, setMe]            = useState<MeResponse | null>(null);
  const [authChecked, setAuth] = useState(false);

  // PR combobox
  const [prs, setPRs]               = useState<PRSummaryResponse[]>([]);
  const [prsLoading, setPRsLoading] = useState(false);
  const [prSearch, setPrSearch]     = useState("");
  const [prOpen, setPrOpen]         = useState(false);

  // Selected PR
  const [selectedPRId, setSelectedPRId] = useState("");
  const [selectedPR, setSelectedPR]     = useState<PRResponse | null>(null);
  const [prLoading, setPRLoading]       = useState(false);

  // Delivery header
  const [deliveryDate, setDeliveryDate] = useState(TODAY);
  const [receivedBy, setReceivedBy]     = useState("");
  const [supplier, setSupplier]         = useState("");
  const [remarks, setRemarks]           = useState("");

  // Item rows
  const [items, setItems]           = useState<ItemRow[]>([]);
  const [deliveredQty, setDeliveredQty] = useState<Record<string, number>>({});

  const [formError, setFormError]   = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted]   = useState<DeliveryResponse | null>(null);

  // ── Auth ────────────────────────────────────────────────────────────────────

  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory) { router.replace("/dashboard"); return; }
        setMe(data);
        setReceivedBy(data.fullName);
        setAuth(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load PR list ────────────────────────────────────────────────────────────

  useEffect(() => {
    if (!authChecked) return;
    setPRsLoading(true);
    api.get<PRSummaryResponse[]>("/purchase-requests")
      .then(({ data }) => setPRs(data.filter((p) => p.status !== "Completed")))
      .catch(() => toast.error("Failed to load PRs", "Could not fetch purchase requests."))
      .finally(() => setPRsLoading(false));
  }, [authChecked]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Load PR detail when selected ────────────────────────────────────────────

  useEffect(() => {
    if (!selectedPRId) { setSelectedPR(null); setItems([]); setDeliveredQty({}); return; }
    setPRLoading(true);
    setFormError(null);

    async function load() {
      const [prRes, summaries] = await Promise.all([
        api.get<PRResponse>(`/purchase-requests/${selectedPRId}`),
        api.get<DeliverySummaryResponse[]>(`/deliveries?prId=${selectedPRId}`)
          .then((r) => r.data).catch(() => [] as DeliverySummaryResponse[]),
      ]);
      const pr = prRes.data;
      setSelectedPR(pr);
      setItems(itemsFromPR(pr));

      const totals: Record<string, number> = {};
      if (summaries.length > 0) {
        const details = await Promise.all(
          summaries.map((s) =>
            api.get<DeliveryResponse>(`/deliveries/${s.id}`)
              .then((r) => r.data).catch(() => null)
          )
        );
        for (const del of details) {
          if (!del) continue;
          for (const item of del.items) {
            totals[item.prItemId] = (totals[item.prItemId] ?? 0) + item.qtyDelivered;
          }
        }
      }
      setDeliveredQty(totals);
    }

    load()
      .catch(() => toast.error("Failed to load PR", "Could not fetch PR details."))
      .finally(() => setPRLoading(false));
  }, [selectedPRId]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── PR combobox helpers ─────────────────────────────────────────────────────

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
    setSelectedPRId(pr.id); setPrSearch(pr.prNo); setPrOpen(false);
  }

  function clearPR() {
    setSelectedPRId(""); setSelectedPR(null);
    setItems([]); setDeliveredQty({});
    setPrSearch(""); setPrOpen(false);
  }

  function patchItem(id: string, patch: Partial<ItemRow>) {
    setItems((rows) => rows.map((r) => r.prItemId === id ? { ...r, ...patch } : r));
  }

  // ── Build payload ───────────────────────────────────────────────────────────

  function buildPayload(): CreateDeliveryRequest | string {
    if (!selectedPRId)      return "Please select a Purchase Request.";
    if (!deliveryDate)      return "Delivery Date is required.";
    if (!receivedBy.trim()) return "Received By is required.";

    const submitItems: CreateDeliveryItemRequest[] = items
      .filter((r) => parseFloat(r.qtyThisDelivery) > 0)
      .map((r) => ({
        prItemId:      r.prItemId,
        qtyDelivered:  parseFloat(r.qtyThisDelivery),
        distributions: [],
      }));

    if (submitItems.length === 0)
      return "At least one item must have a delivery quantity.";

    return {
      prId:         selectedPRId,
      deliveryDate: deliveryDate,
      receivedBy:   receivedBy.trim(),
      supplier:     supplier.trim() || null,
      remarks:      remarks.trim()  || null,
      items:        submitItems,
    };
  }

  async function handleSubmit() {
    setFormError(null);
    const payload = buildPayload();
    if (typeof payload === "string") { setFormError(payload); return; }
    setSubmitting(true);
    try {
      const { data } = await api.post<DeliveryResponse>("/deliveries", payload);
      setSubmitted(data);
      toast.success("Delivery recorded", `Ref: ${data.deliveryRef}`);
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: string } })?.response?.data
        ?? "Failed to submit delivery. Please try again.";
      toast.error("Submission failed", String(msg).slice(0, 150));
    } finally { setSubmitting(false); }
  }

  function handleReset() {
    setSelectedPRId(""); setSelectedPR(null);
    setItems([]); setDeliveredQty({});
    setPrSearch(""); setPrOpen(false);
    setDeliveryDate(TODAY);
    setReceivedBy(me?.fullName ?? "");
    setSupplier(""); setRemarks("");
    setFormError(null); setSubmitted(null);
  }

  // ── Guards ──────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  if (submitted) {
    return (
      <div className="min-h-screen bg-slate-100 flex items-center justify-center p-6">
        <div className="bg-white border border-slate-200 shadow-sm p-10 max-w-md w-full text-center space-y-4">
          <div className="w-14 h-14 rounded-full bg-green-100 flex items-center justify-center mx-auto text-2xl">✅</div>
          <h2 className="text-lg font-bold text-slate-800">Delivery Recorded</h2>
          <p className="text-sm text-slate-500">
            Go to <span className="font-semibold text-green-700">Distribution</span> to record who received the items.
          </p>
          <div className="bg-slate-50 border border-slate-200 px-4 py-3 text-left space-y-1 text-sm">
            <div className="flex justify-between">
              <span className="text-slate-500">Delivery Ref</span>
              <span className="font-mono font-semibold text-slate-800">{submitted.deliveryRef}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-500">Items received</span>
              <span className="text-slate-700">{submitted.items.length}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-500">Delivery Date</span>
              <span className="text-slate-700">{submitted.deliveryDate}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-500">Received By</span>
              <span className="text-slate-700">{submitted.receivedBy}</span>
            </div>
          </div>
          <div className="flex gap-3 justify-center pt-2">
            <button
              onClick={handleReset}
              className="px-5 py-2 text-sm bg-green-600 text-white font-medium hover:bg-green-500 transition-colors"
            >
              Record Another
            </button>
            <button
              onClick={() => router.push("/inventory/distribution")}
              className="px-5 py-2 text-sm border border-green-300 text-green-700 bg-green-50 hover:bg-green-100 transition-colors font-medium"
            >
              Go to Distribution →
            </button>
            <button
              onClick={() => router.push("/inventory")}
              className="px-5 py-2 text-sm border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
            >
              Dashboard
            </button>
          </div>
        </div>
      </div>
    );
  }

  const activeItemCount = items.filter((r) => parseFloat(r.qtyThisDelivery) > 0).length;

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-5">

        {/* Toolbar */}
        <div className="flex items-center justify-end">
          <button
            onClick={handleSubmit}
            disabled={submitting || !selectedPRId}
            className="flex items-center gap-2 px-6 py-2.5 text-sm bg-green-600 text-white font-semibold hover:bg-green-500 shadow-sm transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {submitting
              ? <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              : "✓"}
            {submitting ? "Submitting…" : "Submit Delivery"}
          </button>
        </div>

        {/* Section 1 — Delivery Details */}
        <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
          <SectionHeading number="1" title="Delivery Details" />
          <div className="p-6 grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-4">

            <div className="md:col-span-2">
              <FieldLabel required>Purchase Request</FieldLabel>
              <PRCombobox
                search={prSearch}
                onSearchChange={(v) => { setPrSearch(v); setPrOpen(true); if (!v) clearPR(); }}
                onFocus={() => setPrOpen(true)}
                onBlur={() => setTimeout(() => setPrOpen(false), 150)}
                open={prOpen && prSuggestions.length > 0}
                suggestions={prSuggestions}
                selectedId={selectedPRId}
                onSelect={selectPR}
                loading={prsLoading}
              />
              {selectedPRId && (
                <div className="mt-1.5 flex items-center gap-2">
                  <span className="text-xs text-green-600 font-medium">✓ Selected</span>
                  <button onClick={clearPR} className="text-xs text-slate-400 hover:text-slate-600">Change PR</button>
                </div>
              )}
            </div>

            <div>
              <FieldLabel required>Delivery Date</FieldLabel>
              <YellowInput type="date" value={deliveryDate} onChange={setDeliveryDate} />
            </div>
            <div>
              <FieldLabel>Delivery Ref</FieldLabel>
              <GrayInput value="Auto-generated on submit" />
            </div>

            <div>
              <FieldLabel required>Received By</FieldLabel>
              <YellowInput value={receivedBy} onChange={setReceivedBy} placeholder="Full name" />
            </div>
            <div>
              <FieldLabel>Supplier</FieldLabel>
              <YellowInput value={supplier} onChange={setSupplier} placeholder="Supplier name (optional)" />
            </div>

            <div className="md:col-span-2">
              <FieldLabel>Remarks</FieldLabel>
              <YellowInput value={remarks} onChange={setRemarks} placeholder="Optional delivery notes" />
            </div>

          </div>
        </div>

        {/* Section 2 — Items */}
        {selectedPRId && (
          <div className="bg-white border border-slate-200 shadow-sm overflow-hidden">
            <SectionHeading
              number="2"
              title={selectedPR ? `Items — PR ${selectedPR.prNo}` : "Items"}
            />

            {prLoading ? (
              <div className="flex items-center justify-center py-16">
                <div className="w-7 h-7 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
              </div>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-xs border-collapse">
                  <thead>
                    <tr className="bg-slate-50 border-b border-slate-200 text-slate-500 uppercase tracking-wide">
                      <th className="px-3 py-2.5 text-center font-medium w-10">#</th>
                      <th className="px-3 py-2.5 text-left font-medium w-32">Stock No.</th>
                      <th className="px-3 py-2.5 text-left font-medium">Description</th>
                      <th className="px-3 py-2.5 text-left font-medium w-20">Unit</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Qty Ordered</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Already Received</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Remaining</th>
                      <th className="px-3 py-2.5 text-right font-medium w-36">Qty This Delivery</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {items.length === 0 ? (
                      <tr>
                        <td colSpan={8} className="text-center py-12 text-slate-400">No items on this PR.</td>
                      </tr>
                    ) : items.map((row, i) => {
                      const already    = deliveredQty[row.prItemId] ?? 0;
                      const remaining  = Math.max(0, row.qtyOrdered - already);
                      const thisQty    = parseFloat(row.qtyThisDelivery) || 0;
                      const isFull     = remaining === 0;
                      const overQty    = thisQty > remaining && remaining > 0;
                      return (
                        <tr key={row.prItemId} className={i % 2 === 1 ? "bg-slate-50" : "bg-white"}>
                          <td className="px-3 py-2 text-center text-slate-400">{row.itemNo}</td>
                          <td className="px-3 py-2 font-mono text-slate-600">{row.stockNo ?? "—"}</td>
                          <td className="px-3 py-2 text-slate-800">{row.description}</td>
                          <td className="px-3 py-2 text-slate-600">{row.unit}</td>
                          <td className="px-3 py-2">
                            <div className="px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-500 text-right">
                              {fmt(row.qtyOrdered)}
                            </div>
                          </td>
                          <td className="px-3 py-2">
                            <div className="px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-500 text-right">
                              {already > 0 ? fmt(already) : "—"}
                            </div>
                          </td>
                          <td className="px-3 py-2">
                            <div className={`px-2 py-1.5 border border-slate-200 bg-cell-auto text-right font-medium ${
                              isFull ? "text-red-500" : "text-green-700"
                            }`}>
                              {isFull ? "Fully received" : fmt(remaining)}
                            </div>
                          </td>
                          <td className="px-1.5 py-1.5">
                            <input
                              type="number" min={0} step="any"
                              value={row.qtyThisDelivery}
                              onChange={(e) => patchItem(row.prItemId, { qtyThisDelivery: e.target.value })}
                              placeholder="0"
                              className={`w-full px-2 py-1.5 text-xs border text-right focus:outline-none focus:ring-1 focus:ring-green-500 bg-cell-fill ${
                                overQty ? "border-amber-400 bg-amber-50" : "border-slate-200"
                              }`}
                            />
                            {overQty && (
                              <p className="text-amber-600 text-xs mt-0.5">Exceeds remaining qty</p>
                            )}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>

                <div className="px-4 py-3 border-t border-slate-100 flex items-center justify-between text-xs text-slate-500">
                  <span>{activeItemCount} of {items.length} item{items.length !== 1 ? "s" : ""} with delivery qty</span>
                  {formError && <span className="text-red-500 font-medium">{formError}</span>}
                  <span className="font-semibold text-slate-700">
                    PR Total: ₱{selectedPR ? fmt(selectedPR.totalAmount) : "—"}
                  </span>
                </div>
              </div>
            )}
          </div>
        )}

        {/* Bottom submit */}
        <div className="flex justify-end pb-4">
          <button
            onClick={handleSubmit}
            disabled={submitting || !selectedPRId}
            className="flex items-center gap-2 px-8 py-3 text-sm bg-green-600 text-white font-semibold hover:bg-green-500 shadow-sm transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {submitting
              ? <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              : "✓"}
            {submitting ? "Submitting…" : "Submit Delivery"}
          </button>
        </div>

      </div>
    </div>
  );
}
