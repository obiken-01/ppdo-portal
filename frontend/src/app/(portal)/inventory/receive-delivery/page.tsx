"use client";

/**
 * Receive Delivery page — RAL-54.
 * Matches Penpot frame "05 Receive Delivery".
 *
 * Access guard: canAccessInventory required.
 *
 * Layout:
 *   Section 1 — Delivery Details (PR selector + form fields)
 *   Section 2 — Items table (auto-loaded when PR selected)
 *
 * Split delivery:
 *   Each item row has a "Split" toggle that expands distribution sub-rows.
 *   A distribution row defines which division receives how many units.
 *   QtyDelivered for an item = sum of its distribution QtyIssued values.
 *   A row with no manual QtyThisDelivery entry but with filled distributions
 *   is valid — QtyDelivered is inferred from the distribution sum.
 *   Items with zero total qty and no distributions are excluded from submit.
 *
 * Auto-distribution:
 *   Items that have QtyThisDelivery > 0 but no explicit distributions get
 *   one distribution auto-created on submit: PR's division, full qty,
 *   dateIssued = deliveryDate, issuedBy = receivedBy.
 *
 * API endpoints:
 *   GET  /api/purchase-requests               → PR dropdown (Open + PartiallyDelivered)
 *   GET  /api/purchase-requests/{id}          → PR items when PR selected
 *   POST /api/deliveries                      → submit delivery
 *
 * Cell colours: yellow (bg-cell-fill) = user fills, gray (bg-cell-auto) = read-only.
 */

import { useEffect, useMemo, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import api from "@/lib/api";
import { useToast } from "@/components/ui/Toast";
import type {
  CreateDeliveryItemRequest,
  CreateDeliveryRequest,
  CreateDistributionRequest,
  DeliveryResponse,
  DeliverySummaryResponse,
  Division,
  MeResponse,
  PRResponse,
  PRSummaryResponse,
} from "@/types";

// ---------------------------------------------------------------------------
// Constants
// ---------------------------------------------------------------------------

const DIVISIONS: Division[] = ["Admin", "Planning", "RM", "MIS", "SPD"];
const TODAY = new Date().toISOString().slice(0, 10);


// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmt(n: number) {
  return new Intl.NumberFormat("en-PH", {
    minimumFractionDigits: 2, maximumFractionDigits: 2,
  }).format(n);
}

function uid() { return `${Date.now()}-${Math.random()}`; }

function divName(d: string): Division {
  return d as Division;
}

// ---------------------------------------------------------------------------
// Client-side types
// ---------------------------------------------------------------------------

interface DistRow {
  _id: string;
  division: Division | "";
  qtyIssued: string;
  dateIssued: string;
  issuedBy: string;
  remarks: string;
}

interface ItemRow {
  prItemId: string;
  itemNo: number;
  stockNo: string | null;
  description: string;
  unit: string;
  qtyOrdered: number;
  qtyThisDelivery: string;
  splitOpen: boolean;
  distributions: DistRow[];
}

function blankDist(defaultDate: string, defaultIssuedBy: string): DistRow {
  return {
    _id: uid(), division: "", qtyIssued: "",
    dateIssued: defaultDate, issuedBy: defaultIssuedBy, remarks: "",
  };
}

function itemsFromPR(pr: PRResponse, deliveryDate: string, receivedBy: string): ItemRow[] {
  return pr.items.map((item) => ({
    prItemId:        item.id,
    itemNo:          item.itemNo,
    stockNo:         item.stockNo,
    description:     item.description,
    unit:            item.unit,
    qtyOrdered:      Number(item.quantity),
    qtyThisDelivery: "",
    splitOpen:       false,
    distributions:   [blankDist(deliveryDate, receivedBy)],
  }));
}

// ---------------------------------------------------------------------------
// Small field helpers
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
    <div className="flex items-center gap-3 px-6 py-3 bg-green-600 text-white rounded-t-lg">
      <span className="w-6 h-6 rounded-full bg-white text-green-700 flex items-center justify-center text-xs font-bold shrink-0">
        {number}
      </span>
      <span className="text-sm font-semibold tracking-wide uppercase">{title}</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Distribution sub-row component
// ---------------------------------------------------------------------------

function DistRowUI({
  dist, onChange, onRemove, canRemove, defaultDate, defaultIssuedBy,
}: {
  dist: DistRow;
  onChange: (patch: Partial<DistRow>) => void;
  onRemove: () => void;
  canRemove: boolean;
  defaultDate: string;
  defaultIssuedBy: string;
}) {
  return (
    <tr className="bg-blue-50 border-t border-blue-100">
      <td className="px-2 py-1.5 pl-10 text-xs text-blue-400">↳</td>
      {/* Division */}
      <td className="px-1.5 py-1.5" colSpan={2}>
        <select
          value={dist.division}
          onChange={(e) => onChange({ division: e.target.value as Division })}
          className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill focus:outline-none focus:ring-1 focus:ring-green-500"
        >
          <option value="">— Division —</option>
          {DIVISIONS.map((d) => <option key={d} value={d}>{d}</option>)}
        </select>
      </td>
      {/* Qty Issued */}
      <td className="px-1.5 py-1.5">
        <input
          type="number" min={0} step="any"
          value={dist.qtyIssued}
          onChange={(e) => onChange({ qtyIssued: e.target.value })}
          placeholder="0"
          className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill text-right focus:outline-none focus:ring-1 focus:ring-green-500"
        />
      </td>
      {/* Date Issued */}
      <td className="px-1.5 py-1.5">
        <input
          type="date"
          value={dist.dateIssued || defaultDate}
          onChange={(e) => onChange({ dateIssued: e.target.value })}
          className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill focus:outline-none focus:ring-1 focus:ring-green-500"
        />
      </td>
      {/* Issued By */}
      <td className="px-1.5 py-1.5">
        <input
          type="text"
          value={dist.issuedBy || defaultIssuedBy}
          onChange={(e) => onChange({ issuedBy: e.target.value })}
          placeholder="Issued by"
          className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill focus:outline-none focus:ring-1 focus:ring-green-500"
        />
      </td>
      {/* Remarks */}
      <td className="px-1.5 py-1.5">
        <input
          type="text"
          value={dist.remarks}
          onChange={(e) => onChange({ remarks: e.target.value })}
          placeholder="Remarks"
          className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill focus:outline-none focus:ring-1 focus:ring-green-500"
        />
      </td>
      <td className="px-1.5 py-1.5 text-center">
        <button
          onClick={onRemove} disabled={!canRemove} title="Remove distribution"
          className="text-slate-300 hover:text-red-500 disabled:opacity-20 transition-colors text-sm"
        >✕</button>
      </td>
    </tr>
  );
}

// ---------------------------------------------------------------------------
// PR Combobox — typeable input with filtered suggestion list
// ---------------------------------------------------------------------------

const STATUS_BADGE: Record<string, string> = {
  Open:                "bg-blue-100 text-blue-700",
  PartiallyDelivered:  "bg-amber-100 text-amber-700",
  FullyDelivered:      "bg-green-100 text-green-700",
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
          type="text"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          onFocus={onFocus}
          onBlur={onBlur}
          placeholder={loading ? "Loading PRs…" : "Type PR number, division, or status to search…"}
          disabled={loading}
          className="flex-1 px-3 py-2 text-sm bg-transparent outline-none disabled:opacity-60"
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
                <span className="text-xs text-slate-400">{divName(pr.division)}</span>
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

  const [me, setMe]             = useState<MeResponse | null>(null);
  const [authChecked, setAuth]  = useState(false);

  // PR list + combobox
  const [prs, setPRs]               = useState<PRSummaryResponse[]>([]);
  const [prsLoading, setPRsLoading] = useState(false);
  const [prSearch, setPrSearch]     = useState("");  // typed text in combobox
  const [prOpen, setPrOpen]         = useState(false);

  // Selected PR + its items
  const [selectedPRId, setSelectedPRId] = useState("");
  const [selectedPR, setSelectedPR]     = useState<PRResponse | null>(null);
  const [prLoading, setPRLoading]       = useState(false);

  // Delivery form header
  const [deliveryDate, setDeliveryDate] = useState(TODAY);
  const [receivedBy, setReceivedBy]     = useState("");
  const [supplier, setSupplier]         = useState("");
  const [remarks, setRemarks]           = useState("");

  // Item rows
  const [items, setItems] = useState<ItemRow[]>([]);
  // deliveredQty[prItemId] = total qty already delivered across all previous deliveries
  const [deliveredQty, setDeliveredQty] = useState<Record<string, number>>({});

  // Form errors
  const [formError, setFormError] = useState<string | null>(null);

  // Submit
  const [submitting, setSubmitting] = useState(false);
  const [submitted, setSubmitted]   = useState<DeliveryResponse | null>(null);

  // ── Auth guard ─────────────────────────────────────────────────────────────

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

  // ── Load PR list ───────────────────────────────────────────────────────────

  useEffect(() => {
    if (!authChecked) return;
    setPRsLoading(true);
    api.get<PRSummaryResponse[]>("/purchase-requests")
      .then(({ data }) => {
        // Exclude only Completed — backend blocks Completed at submit time.
        // Open, PartiallyDelivered and FullyDelivered can all receive deliveries.
        setPRs(data.filter((p) => p.status !== "Completed"));
      })
      .catch(() => toast.error("Failed to load PRs", "Could not fetch purchase requests."))
      .finally(() => setPRsLoading(false));
  }, [authChecked]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Load PR items + delivery history when selection changes ──────────────

  useEffect(() => {
    if (!selectedPRId) {
      setSelectedPR(null);
      setItems([]);
      setDeliveredQty({});
      return;
    }
    setPRLoading(true);
    setFormError(null);

    async function load() {
      // Fetch PR detail and past deliveries in parallel
      const [prRes, deliverySummaries] = await Promise.all([
        api.get<PRResponse>(`/purchase-requests/${selectedPRId}`),
        api.get<DeliverySummaryResponse[]>(`/deliveries?prId=${selectedPRId}`)
          .then((r) => r.data)
          .catch(() => [] as DeliverySummaryResponse[]),  // non-fatal
      ]);

      const pr = prRes.data;
      setSelectedPR(pr);
      setItems(itemsFromPR(pr, deliveryDate, receivedBy));

      // Fetch each delivery's full detail to get per-item QtyDelivered
      const totals: Record<string, number> = {};
      if (deliverySummaries.length > 0) {
        const detailed = await Promise.all(
          deliverySummaries.map((s) =>
            api.get<DeliveryResponse>(`/deliveries/${s.id}`)
              .then((r) => r.data)
              .catch(() => null)
          )
        );
        for (const delivery of detailed) {
          if (!delivery) continue;
          for (const item of delivery.items) {
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

  // ── PR combobox selection ──────────────────────────────────────────────────

  function selectPR(pr: PRSummaryResponse) {
    setSelectedPRId(pr.id);
    setPrSearch(pr.prNo);  // show PRNo in the input after selection
    setPrOpen(false);
  }

  function clearPR() {
    setSelectedPRId("");
    setSelectedPR(null);
    setItems([]);
    setDeliveredQty({});
    setPrSearch("");
    setPrOpen(false);
  }

  // Suggestions: match search text against prNo, division name, status
  const prSuggestions = useMemo(() => {
    const q = prSearch.toLowerCase().trim();
    if (!q) return prs;
    return prs.filter((p) =>
      p.prNo.toLowerCase().includes(q) ||
      divName(p.division).toLowerCase().includes(q) ||
      p.status.toLowerCase().includes(q) ||
      p.requestedBy.toLowerCase().includes(q)
    );
  }, [prs, prSearch]);

  // ── Item / distribution patching ───────────────────────────────────────────

  function patchItem(id: string, patch: Partial<ItemRow>) {
    setItems((rows) => rows.map((r) => r.prItemId === id ? { ...r, ...patch } : r));
  }

  function patchDist(itemId: string, distId: string, patch: Partial<DistRow>) {
    setItems((rows) => rows.map((r) => {
      if (r.prItemId !== itemId) return r;
      return {
        ...r,
        distributions: r.distributions.map((d) =>
          d._id === distId ? { ...d, ...patch } : d
        ),
      };
    }));
  }

  function addDist(itemId: string) {
    setItems((rows) => rows.map((r) => {
      if (r.prItemId !== itemId) return r;
      return { ...r, distributions: [...r.distributions, blankDist(deliveryDate, receivedBy)] };
    }));
  }

  function removeDist(itemId: string, distId: string) {
    setItems((rows) => rows.map((r) => {
      if (r.prItemId !== itemId) return r;
      return { ...r, distributions: r.distributions.filter((d) => d._id !== distId) };
    }));
  }

  // ── Computed totals ────────────────────────────────────────────────────────

  const activeItems = useMemo(() => {
    return items.filter((r) => {
      const mainQty = parseFloat(r.qtyThisDelivery) || 0;
      const distSum = r.splitOpen
        ? r.distributions.reduce((s, d) => s + (parseFloat(d.qtyIssued) || 0), 0)
        : 0;
      return mainQty > 0 || distSum > 0;
    });
  }, [items]);

  function itemQtyDelivered(row: ItemRow): number {
    if (row.splitOpen) {
      const distSum = row.distributions.reduce((s, d) => s + (parseFloat(d.qtyIssued) || 0), 0);
      if (distSum > 0) return distSum;
    }
    return parseFloat(row.qtyThisDelivery) || 0;
  }

  // ── Validate & build payload ───────────────────────────────────────────────

  function buildPayload(): CreateDeliveryRequest | string {
    if (!selectedPRId) return "Please select a Purchase Request.";
    if (!deliveryDate)  return "Delivery Date is required.";
    if (!receivedBy.trim()) return "Received By is required.";

    const submitItems: CreateDeliveryItemRequest[] = [];

    for (const row of items) {
      const qty = itemQtyDelivered(row);
      if (qty <= 0) continue; // skip rows with no qty

      let dists: CreateDistributionRequest[];

      if (row.splitOpen && row.distributions.some((d) => parseFloat(d.qtyIssued) > 0)) {
        // Validate distributions
        for (const d of row.distributions) {
          const dQty = parseFloat(d.qtyIssued) || 0;
          if (dQty <= 0) continue;
          if (!d.division) return `Row ${row.itemNo} (${row.description}): Division is required in distribution.`;
          if (!d.issuedBy.trim() && !receivedBy.trim()) return `Row ${row.itemNo}: Issued By is required.`;
        }

        dists = row.distributions
          .filter((d) => parseFloat(d.qtyIssued) > 0)
          .map((d): CreateDistributionRequest => ({
            division:   d.division as string,
            qtyIssued:  parseFloat(d.qtyIssued),
            dateIssued: d.dateIssued || deliveryDate,
            issuedBy:   d.issuedBy.trim() || receivedBy.trim(),
            remarks:    d.remarks.trim() || null,
          }));
      } else {
        // Auto-distribution: use PR's division, full qty
        const prDivision = selectedPR ? selectedPR.division : "Admin";
        dists = [{
          division:   prDivision,
          qtyIssued:  qty,
          dateIssued: deliveryDate,
          issuedBy:   receivedBy.trim(),
          remarks:    null,
        }];
      }

      submitItems.push({ prItemId: row.prItemId, qtyDelivered: qty, distributions: dists });
    }

    if (submitItems.length === 0) return "At least one item must have a delivery quantity.";

    return {
      prId:         selectedPRId,
      deliveryDate: deliveryDate,
      receivedBy:   receivedBy.trim(),
      supplier:     supplier.trim() || null,
      remarks:      remarks.trim()  || null,
      items:        submitItems,
    };
  }

  // ── Submit ─────────────────────────────────────────────────────────────────

  async function handleSubmit() {
    setFormError(null);
    const payload = buildPayload();
    if (typeof payload === "string") { setFormError(payload); return; }

    setSubmitting(true);
    try {
      const { data } = await api.post<DeliveryResponse>("/deliveries", payload);
      setSubmitted(data);
      toast.success("Delivery recorded", `Delivery Ref: ${data.deliveryRef}`);
    } catch (e: unknown) {
      const msg =
        (e as { response?: { data?: string } })?.response?.data ??
        "Failed to submit delivery. Please try again.";
      toast.error("Submission failed", String(msg).slice(0, 150));
    } finally {
      setSubmitting(false);
    }
  }

  // ── Reset ──────────────────────────────────────────────────────────────────

  function handleReset() {
    setSelectedPRId("");
    setSelectedPR(null);
    setItems([]);
    setDeliveredQty({});
    setPrSearch("");
    setPrOpen(false);
    setDeliveryDate(TODAY);
    setReceivedBy(me?.fullName ?? "");
    setSupplier("");
    setRemarks("");
    setFormError(null);
    setSubmitted(null);
  }

  // ── Guards ─────────────────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="min-h-screen flex items-center justify-center bg-slate-100">
        <div className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  // ── Success state ──────────────────────────────────────────────────────────

  if (submitted) {
    return (
      <div className="min-h-screen bg-slate-100 flex items-center justify-center p-6">
        <div className="bg-white border border-slate-200 shadow-sm rounded-xl p-10 max-w-md w-full text-center space-y-4">
          <div className="w-14 h-14 rounded-full bg-green-100 flex items-center justify-center mx-auto text-2xl">✅</div>
          <h2 className="text-lg font-bold text-slate-800">Delivery Recorded</h2>
          <div className="bg-slate-50 border border-slate-200 rounded-lg px-4 py-3 text-left space-y-1 text-sm">
            <div className="flex justify-between">
              <span className="text-slate-500">Delivery Ref</span>
              <span className="font-mono font-semibold text-slate-800">{submitted.deliveryRef}</span>
            </div>
            <div className="flex justify-between">
              <span className="text-slate-500">Items</span>
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
              className="px-5 py-2 text-sm rounded-lg bg-green-600 text-white font-medium hover:bg-green-500 transition-colors"
            >
              Record Another
            </button>
            <button
              onClick={() => router.push("/inventory")}
              className="px-5 py-2 text-sm rounded-lg border border-slate-200 text-slate-600 hover:bg-slate-50 transition-colors"
            >
              Back to Dashboard
            </button>
          </div>
        </div>
      </div>
    );
  }

  // ── Main render ────────────────────────────────────────────────────────────

  return (
    <div className="min-h-screen bg-slate-100 font-sans">
      <div className="max-w-screen-xl mx-auto px-6 py-6 space-y-5">

        {/* ── Toolbar ──────────────────────────────────────────────────────── */}
        <div className="flex items-center justify-end">
          <button
            onClick={handleSubmit} disabled={submitting || !selectedPRId}
            className="flex items-center gap-2 px-6 py-2.5 text-sm rounded-lg bg-green-600 text-white font-semibold hover:bg-green-500 shadow-sm transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {submitting
              ? <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              : <span>✓</span>}
            {submitting ? "Submitting…" : "Submit Delivery"}
          </button>
        </div>

        {/* ── Section 1 — Delivery Details ─────────────────────────────────── */}
        <div className="bg-white border border-slate-200 shadow-sm rounded-lg overflow-hidden">
          <SectionHeading number="1" title="Delivery Details" />
          <div className="p-6 grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-4">

            {/* PR Selector — searchable combobox */}
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
                  <button onClick={clearPR} className="text-xs text-slate-400 hover:text-slate-600 transition-colors">
                    Change PR
                  </button>
                </div>
              )}
            </div>

            {/* Delivery Date | Delivery Ref */}
            <div>
              <FieldLabel required>Delivery Date</FieldLabel>
              <YellowInput type="date" value={deliveryDate} onChange={setDeliveryDate} />
            </div>
            <div>
              <FieldLabel>Delivery Ref</FieldLabel>
              <GrayInput value="Auto-generated on submit" />
            </div>

            {/* Received By | Supplier */}
            <div>
              <FieldLabel required>Received By</FieldLabel>
              <YellowInput value={receivedBy} onChange={setReceivedBy} placeholder="Full name" />
            </div>
            <div>
              <FieldLabel>Supplier</FieldLabel>
              <YellowInput value={supplier} onChange={setSupplier} placeholder="Supplier name (optional)" />
            </div>

            {/* Remarks — full width */}
            <div className="md:col-span-2">
              <FieldLabel>Remarks</FieldLabel>
              <YellowInput value={remarks} onChange={setRemarks} placeholder="Optional delivery notes" />
            </div>

          </div>
        </div>

        {/* ── Section 2 — Items ─────────────────────────────────────────────── */}
        {selectedPRId && (
          <div className="bg-white border border-slate-200 shadow-sm rounded-lg overflow-hidden">
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
                      <th className="px-3 py-2.5 text-left font-medium min-w-52">Description</th>
                      <th className="px-3 py-2.5 text-left font-medium w-20">Unit</th>
                      <th className="px-3 py-2.5 text-right font-medium w-24">Qty Ordered</th>
                      <th className="px-3 py-2.5 text-right font-medium w-24">Remaining</th>
                      <th className="px-3 py-2.5 text-right font-medium w-32">Qty This Delivery</th>
                      <th className="px-3 py-2.5 text-right font-medium w-28">Date Issued</th>
                      <th className="px-3 py-2.5 text-left font-medium w-28">Issued By</th>
                      <th className="px-3 py-2.5 text-left font-medium w-24">Remarks</th>
                      <th className="px-3 py-2.5 w-16 text-center font-medium">Split</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {items.length === 0 ? (
                      <tr>
                        <td colSpan={10} className="text-center py-12 text-slate-400">
                          No items found on this PR.
                        </td>
                      </tr>
                    ) : items.map((row, idx) => (
                      <>
                        {/* ── Item row ── */}
                        <tr
                          key={row.prItemId}
                          className={`transition-colors ${idx % 2 === 1 ? "bg-slate-50" : "bg-white"} ${row.splitOpen ? "border-b-0" : ""}`}
                        >
                          <td className="px-3 py-2 text-center text-slate-400">{row.itemNo}</td>

                          <td className="px-3 py-2">
                            <span className="font-mono text-slate-600">{row.stockNo ?? "—"}</span>
                          </td>

                          <td className="px-3 py-2 text-slate-800">{row.description}</td>

                          <td className="px-3 py-2 text-slate-600">{row.unit}</td>

                          {/* Qty Ordered — gray */}
                          <td className="px-1.5 py-1.5 text-right">
                            <div className="w-full px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-500 text-right select-none">
                              {fmt(row.qtyOrdered)}
                            </div>
                          </td>

                          {/* Remaining — gray, computed from previous deliveries */}
                          {(() => {
                            const already  = deliveredQty[row.prItemId] ?? 0;
                            const remaining = Math.max(0, row.qtyOrdered - already);
                            const isFull    = remaining === 0;
                            return (
                              <td className="px-1.5 py-1.5 text-right">
                                <div className={`w-full px-2 py-1.5 border border-slate-200 bg-cell-auto text-right select-none font-medium ${
                                  isFull ? "text-red-500" : "text-green-700"
                                }`}>
                                  {isFull ? "Full" : fmt(remaining)}
                                </div>
                              </td>
                            );
                          })()}

                          {/* Qty This Delivery — yellow (hidden when split open and has distributions) */}
                          <td className="px-1.5 py-1.5">
                            {row.splitOpen ? (
                              <div className="w-full px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-400 text-right select-none text-xs">
                                {fmt(row.distributions.reduce((s, d) => s + (parseFloat(d.qtyIssued) || 0), 0))}
                                <span className="ml-1 text-blue-400 text-xs">(split)</span>
                              </div>
                            ) : (
                              <input
                                type="number" min={0} step="any"
                                value={row.qtyThisDelivery}
                                onChange={(e) => patchItem(row.prItemId, { qtyThisDelivery: e.target.value })}
                                placeholder="0"
                                className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-fill text-right focus:outline-none focus:ring-1 focus:ring-green-500"
                              />
                            )}
                          </td>

                          {/* Date Issued — yellow, default to deliveryDate */}
                          <td className="px-1.5 py-1.5">
                            {row.splitOpen ? (
                              <div className="w-full px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-400 text-xs select-none">per split</div>
                            ) : (
                              <input
                                type="date"
                                value={deliveryDate}
                                readOnly tabIndex={-1}
                                className="w-full px-2 py-1.5 text-xs border border-slate-200 bg-cell-auto text-slate-500 cursor-default"
                              />
                            )}
                          </td>

                          {/* Issued By — gray, comes from receivedBy */}
                          <td className="px-1.5 py-1.5">
                            {row.splitOpen ? (
                              <div className="w-full px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-400 text-xs select-none truncate">per split</div>
                            ) : (
                              <div className="w-full px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-500 text-xs truncate select-none">
                                {receivedBy || "—"}
                              </div>
                            )}
                          </td>

                          {/* Remarks */}
                          <td className="px-1.5 py-1.5">
                            <div className="w-full px-2 py-1.5 border border-slate-200 bg-cell-auto text-slate-400 text-xs select-none">—</div>
                          </td>

                          {/* Split toggle */}
                          <td className="px-1.5 py-1.5 text-center">
                            <button
                              onClick={() => patchItem(row.prItemId, { splitOpen: !row.splitOpen })}
                              title={row.splitOpen ? "Collapse split" : "Split by division"}
                              className={`px-2 py-1 text-xs rounded border transition-colors ${
                                row.splitOpen
                                  ? "bg-blue-100 border-blue-300 text-blue-700 font-semibold"
                                  : "bg-white border-slate-200 text-slate-500 hover:bg-slate-50"
                              }`}
                            >
                              🔀
                            </button>
                          </td>
                        </tr>

                        {/* ── Distribution sub-rows ── */}
                        {row.splitOpen && row.distributions.map((dist) => (
                          <DistRowUI
                            key={dist._id}
                            dist={dist}
                            onChange={(patch) => patchDist(row.prItemId, dist._id, patch)}
                            onRemove={() => removeDist(row.prItemId, dist._id)}
                            canRemove={row.distributions.length > 1}
                            defaultDate={deliveryDate}
                            defaultIssuedBy={receivedBy}
                          />
                        ))}

                        {/* ── Add Distribution row ── */}
                        {row.splitOpen && (
                          <tr key={`add-dist-${row.prItemId}`} className="bg-blue-50">
                            <td colSpan={10} className="px-4 py-2 pl-10">
                              <button
                                onClick={() => addDist(row.prItemId)}
                                className="text-xs text-blue-600 hover:text-blue-800 font-medium flex items-center gap-1 transition-colors"
                              >
                                <span className="text-base leading-none">+</span> Add Division
                              </button>
                            </td>
                          </tr>
                        )}
                      </>
                    ))}
                  </tbody>
                </table>

                {/* Footer */}
                <div className="px-4 py-3 border-t border-slate-100 flex items-center justify-between text-xs text-slate-500">
                  <span>
                    {activeItems.length} of {items.length} item{items.length !== 1 ? "s" : ""} with delivery qty
                  </span>
                  {formError && (
                    <span className="text-red-500 font-medium flex-1 mx-4">{formError}</span>
                  )}
                  <span className="font-semibold text-slate-700">
                    PR Total: ₱ {selectedPR ? fmt(selectedPR.totalAmount) : "—"}
                  </span>
                </div>
              </div>
            )}
          </div>
        )}

        {/* Bottom submit */}
        <div className="flex justify-end pb-4">
          <button
            onClick={handleSubmit} disabled={submitting || !selectedPRId}
            className="flex items-center gap-2 px-8 py-3 text-sm rounded-lg bg-green-600 text-white font-semibold hover:bg-green-500 shadow-sm transition-colors disabled:opacity-60 disabled:cursor-not-allowed"
          >
            {submitting
              ? <span className="w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
              : <span>✓</span>}
            {submitting ? "Submitting…" : "Submit Delivery"}
          </button>
        </div>

      </div>
    </div>
  );
}
