"use client";

/**
 * Inventory Dashboard page — RAL-56.
 * Matches Penpot frame "04 Inventory Dashboard".
 *
 * Access guard: canAccessInventory.
 *
 * Layout:
 *   Page header  — title + subtitle
 *   Stat groups  — Group 1 (PR counts) + Group 2 (Inventory Alerts) side by side
 *   PR table     — all PRs with status badge, fulfillment bar, quick actions
 *   Alert table  — low / out-of-stock items (from /api/inventory/ledger)
 *
 * API:
 *   GET /api/auth/me                  → permission check
 *   GET /api/inventory/stats          → InventoryStatsResponse (two groups)
 *   GET /api/purchase-requests        → PRSummaryResponse[]   (PR status table)
 *   GET /api/inventory/ledger         → ItemLedgerRowResponse[] (alerts table)
 */

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import Link from "next/link";
import api from "@/lib/api";
import { useToast } from "@/components/ui/Toast";
import type {
  MeResponse,
  PRSummaryResponse,
  InventoryStatsResponse,
  ItemLedgerRowResponse,
} from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function fmtCurrency(n: number) {
  return new Intl.NumberFormat("en-PH", {
    style: "currency",
    currency: "PHP",
    minimumFractionDigits: 2,
  }).format(n);
}

function fmtDate(d: string) {
  if (!d) return "—";
  return d.slice(0, 10);
}

const STATUS_BADGE: Record<string, string> = {
  Open:               "bg-info-100 text-info-500 border border-blue-200",
  PartiallyDelivered: "bg-amber-100 text-amber-500 border border-amber-200",
  FullyDelivered:     "bg-green-50 text-green-600 border border-green-200",
  Completed:          "bg-slate-100 text-slate-600 border border-slate-200",
};

// ---------------------------------------------------------------------------
// StatCard
// ---------------------------------------------------------------------------

function StatCard({
  label,
  value,
  bg,
  textColor,
  icon,
  sub,
}: {
  label: string;
  value: string | number;
  bg: string;
  textColor: string;
  icon: string;
  sub?: string;
}) {
  return (
    <div className={`flex-1 min-w-0 rounded-lg p-4 ${bg} border border-white/60`}>
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="text-xs font-medium text-slate-500 truncate">{label}</p>
          <p className={`text-2xl font-bold mt-1 ${textColor}`}>{value}</p>
          {sub && <p className="text-xs text-slate-400 mt-0.5">{sub}</p>}
        </div>
        <span className="text-xl shrink-0 opacity-70">{icon}</span>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// StatGroup
// ---------------------------------------------------------------------------

function StatGroup({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <div className="flex-1 min-w-0 bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
      <div className="px-4 py-2.5 bg-green-600 text-white text-xs font-semibold tracking-wide uppercase">
        {title}
      </div>
      <div className="flex gap-3 p-3">{children}</div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Fulfillment bar
// ---------------------------------------------------------------------------

function FulfillmentBar({
  pr,
  ledger,
}: {
  pr: PRSummaryResponse;
  ledger: ItemLedgerRowResponse[];
}) {
  // We don't have per-PR fulfillment % from the summary endpoint,
  // so derive it from status as a visual indicator.
  const pct =
    pr.status === "FullyDelivered" || pr.status === "Completed"
      ? 100
      : pr.status === "PartiallyDelivered"
      ? 50   // approximate — real % requires report endpoint
      : 0;

  // Suppress "ledger unused" lint warning — passed for potential future use
  void ledger;

  const barColor =
    pct === 100 ? "bg-green-400" : pct > 0 ? "bg-amber-400" : "bg-slate-200";

  return (
    <div className="flex items-center gap-2 min-w-[80px]">
      <div className="flex-1 h-1.5 bg-slate-100 rounded-full overflow-hidden">
        <div
          className={`h-full rounded-full transition-all ${barColor}`}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="text-xs text-slate-500 w-8 text-right">{pct}%</span>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function InventoryDashboardPage() {
  const router    = useRouter();
  const { toast } = useToast();

  const [authChecked, setAuthChecked] = useState(false);

  const [stats,       setStats]       = useState<InventoryStatsResponse | null>(null);
  const [statsLoading, setStatsLoading] = useState(false);

  const [prs,         setPRs]         = useState<PRSummaryResponse[]>([]);
  const [prsLoading,  setPRsLoading]  = useState(false);

  const [ledger,      setLedger]      = useState<ItemLedgerRowResponse[]>([]);
  const [ledgerLoading, setLedgerLoading] = useState(false);

  // ── Auth guard ─────────────────────────────────────────────────────────────

  useEffect(() => {
    api.get<MeResponse>("/auth/me")
      .then(({ data }) => {
        if (!data.canAccessInventory) {
          router.replace("/dashboard");
          return;
        }
        setAuthChecked(true);
      })
      .catch(() => router.replace("/login"));
  }, [router]);

  // ── Load data ──────────────────────────────────────────────────────────────

  useEffect(() => {
    if (!authChecked) return;

    setStatsLoading(true);
    api.get<InventoryStatsResponse>("/inventory/stats")
      .then(({ data }) => setStats(data))
      .catch(() => toast.error("Failed to load stats", "Could not fetch inventory stats."))
      .finally(() => setStatsLoading(false));

    setPRsLoading(true);
    api.get<PRSummaryResponse[]>("/purchase-requests")
      .then(({ data }) => setPRs(data))
      .catch(() => toast.error("Failed to load PRs", "Could not fetch purchase requests."))
      .finally(() => setPRsLoading(false));

    setLedgerLoading(true);
    api.get<ItemLedgerRowResponse[]>("/inventory/ledger")
      .then(({ data }) => setLedger(data))
      .catch(() => toast.error("Failed to load ledger", "Could not fetch item ledger."))
      .finally(() => setLedgerLoading(false));
  }, [authChecked]); // eslint-disable-line react-hooks/exhaustive-deps

  // ── Derived ────────────────────────────────────────────────────────────────

  const alertItems = ledger.filter((r) => r.isLowStock || r.isOutOfStock);

  // ── Loading skeleton ───────────────────────────────────────────────────────

  if (!authChecked) {
    return (
      <div className="flex items-center justify-center h-64">
        <span className="w-8 h-8 border-4 border-green-600 border-t-transparent rounded-full animate-spin" />
      </div>
    );
  }

  const pr = stats?.purchaseRequests;
  const al = stats?.inventoryAlerts;

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <div className="flex flex-col gap-6 p-6 bg-slate-100 min-h-full">

      {/* ── Page header ──────────────────────────────────────────────────── */}
      <div className="flex items-start justify-between flex-wrap gap-3">
        <div>
          <h1 className="text-xl font-bold text-slate-800 flex items-center gap-2">
            <span>📦</span> Inventory Dashboard
          </h1>
          <p className="text-sm text-slate-500 mt-0.5">
            Overview of purchase requests, delivery status, and stock levels.
          </p>
        </div>

        {/* Quick actions */}
        <div className="flex gap-2 flex-wrap">
          <Link
            href="/inventory/create-pr"
            className="flex items-center gap-1.5 px-3 py-2 bg-green-600 hover:bg-green-500 text-white text-sm font-medium rounded transition-colors"
          >
            <span>📋</span> Create PR
          </Link>
          <Link
            href="/inventory/receive-delivery"
            className="flex items-center gap-1.5 px-3 py-2 bg-white border border-slate-200 hover:bg-slate-50 text-slate-700 text-sm font-medium rounded transition-colors"
          >
            <span>🚚</span> Receive Delivery
          </Link>
          <Link
            href="/inventory/pr-report"
            className="flex items-center gap-1.5 px-3 py-2 bg-white border border-slate-200 hover:bg-slate-50 text-slate-700 text-sm font-medium rounded transition-colors"
          >
            <span>📊</span> PR Report
          </Link>
        </div>
      </div>

      {/* ── Stat card groups ─────────────────────────────────────────────── */}
      <div className="flex gap-4 flex-wrap">

        {/* Group 1 — Purchase Requests */}
        <StatGroup title="📋  Purchase Requests">
          <StatCard
            label="Total PRs"
            value={statsLoading ? "—" : (pr?.total ?? 0)}
            bg="bg-slate-50"
            textColor="text-slate-800"
            icon="📄"
          />
          <StatCard
            label="Open"
            value={statsLoading ? "—" : (pr?.open ?? 0)}
            bg="bg-[#EBF4FF]"
            textColor="text-info-500"
            icon="🔵"
          />
          <StatCard
            label="Partially Delivered"
            value={statsLoading ? "—" : (pr?.partiallyDelivered ?? 0)}
            bg="bg-[#FEF9EC]"
            textColor="text-amber-500"
            icon="🟡"
          />
          <StatCard
            label="Completed"
            value={statsLoading ? "—" : (pr?.fullyDeliveredOrCompleted ?? 0)}
            bg="bg-green-50"
            textColor="text-green-600"
            icon="✅"
          />
        </StatGroup>

        {/* Group 2 — Inventory Alerts */}
        <StatGroup title="📦  Inventory Alerts">
          <StatCard
            label="In Stock"
            value={statsLoading ? "—" : (al?.inStock ?? 0)}
            bg="bg-green-50"
            textColor="text-green-600"
            icon="✅"
            sub="items"
          />
          <StatCard
            label="Low / Out of Stock"
            value={statsLoading ? "—" : (al?.lowOrOutOfStock ?? 0)}
            bg="bg-[#FEF2F2]"
            textColor="text-danger-500"
            icon="⚠️"
            sub="items"
          />
          <StatCard
            label="Total PR Value"
            value={statsLoading ? "—" : fmtCurrency(al?.totalPRValue ?? 0)}
            bg="bg-slate-50"
            textColor="text-amber-500"
            icon="💰"
          />
          <StatCard
            label="Unique Items Tracked"
            value={statsLoading ? "—" : (al?.uniqueItemsTracked ?? 0)}
            bg="bg-[#F3F0FF]"
            textColor="text-purple-700"
            icon="🗂️"
            sub="items"
          />
        </StatGroup>

      </div>

      {/* ── PR Status Table ───────────────────────────────────────────────── */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-100">
          <h2 className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <span>📋</span> Purchase Request Status
          </h2>
          <Link
            href="/inventory/pr-report"
            className="text-xs text-green-600 hover:underline font-medium"
          >
            View Reports →
          </Link>
        </div>

        {prsLoading ? (
          <div className="flex items-center justify-center py-12">
            <span className="w-6 h-6 border-2 border-green-600 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : prs.length === 0 ? (
          <div className="py-12 text-center text-sm text-slate-400">
            No purchase requests found.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-slate-50 text-xs text-slate-500 uppercase tracking-wide border-b border-slate-100">
                  <th className="px-4 py-2.5 text-left font-semibold">PR No.</th>
                  <th className="px-4 py-2.5 text-left font-semibold">Division</th>
                  <th className="px-4 py-2.5 text-left font-semibold">Requested By</th>
                  <th className="px-4 py-2.5 text-left font-semibold">PR Date</th>
                  <th className="px-4 py-2.5 text-left font-semibold">Status</th>
                  <th className="px-4 py-2.5 text-left font-semibold">Fulfillment</th>
                  <th className="px-4 py-2.5 text-right font-semibold">Total Amount</th>
                  <th className="px-4 py-2.5 text-left font-semibold">Actions</th>
                </tr>
              </thead>
              <tbody>
                {prs.map((pr, idx) => (
                  <tr
                    key={pr.id}
                    className={`border-b border-slate-50 last:border-0 transition-colors hover:bg-green-50/50 ${
                      idx % 2 === 0 ? "bg-white" : "bg-slate-50/50"
                    }`}
                  >
                    <td className="px-4 py-2.5 font-mono text-xs font-semibold text-slate-800 whitespace-nowrap">
                      {pr.prNo}
                    </td>
                    <td className="px-4 py-2.5 text-slate-600 text-xs">{pr.division}</td>
                    <td className="px-4 py-2.5 text-slate-600 text-xs max-w-[160px] truncate">
                      {pr.requestedBy}
                    </td>
                    <td className="px-4 py-2.5 text-slate-500 text-xs whitespace-nowrap">
                      {fmtDate(pr.prDate)}
                    </td>
                    <td className="px-4 py-2.5">
                      <span
                        className={`inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium whitespace-nowrap ${
                          STATUS_BADGE[pr.status] ?? "bg-slate-100 text-slate-600"
                        }`}
                      >
                        {pr.status === "FullyDelivered"
                          ? "Fully Delivered"
                          : pr.status === "PartiallyDelivered"
                          ? "Partially Delivered"
                          : pr.status}
                      </span>
                    </td>
                    <td className="px-4 py-2.5 min-w-[100px]">
                      <FulfillmentBar pr={pr} ledger={ledger} />
                    </td>
                    <td className="px-4 py-2.5 text-right text-xs font-medium text-slate-700 whitespace-nowrap">
                      {fmtCurrency(pr.totalAmount)}
                    </td>
                    <td className="px-4 py-2.5">
                      <Link
                        href={`/inventory/pr-report?id=${pr.id}`}
                        className="text-xs text-green-600 hover:underline font-medium"
                      >
                        Report
                      </Link>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* ── Inventory Alerts Table ────────────────────────────────────────── */}
      <div className="bg-white rounded-xl border border-slate-200 shadow-sm overflow-hidden">
        <div className="flex items-center justify-between px-4 py-3 border-b border-slate-100">
          <h2 className="text-sm font-semibold text-slate-700 flex items-center gap-2">
            <span>⚠️</span> Inventory Alerts
            {alertItems.length > 0 && (
              <span className="ml-1 px-2 py-0.5 bg-danger-100 text-danger-500 text-xs font-semibold rounded-full">
                {alertItems.length}
              </span>
            )}
          </h2>
          <Link
            href="/inventory/item-ledger"
            className="text-xs text-green-600 hover:underline font-medium"
          >
            View Full Ledger →
          </Link>
        </div>

        {ledgerLoading ? (
          <div className="flex items-center justify-center py-12">
            <span className="w-6 h-6 border-2 border-green-600 border-t-transparent rounded-full animate-spin" />
          </div>
        ) : alertItems.length === 0 ? (
          <div className="py-10 text-center">
            <p className="text-sm text-green-600 font-medium">✅ All items are sufficiently stocked.</p>
            <p className="text-xs text-slate-400 mt-1">No low or out-of-stock items detected.</p>
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="bg-slate-50 text-xs text-slate-500 uppercase tracking-wide border-b border-slate-100">
                  <th className="px-4 py-2.5 text-left font-semibold">Stock No.</th>
                  <th className="px-4 py-2.5 text-left font-semibold">Description</th>
                  <th className="px-4 py-2.5 text-left font-semibold">Unit</th>
                  <th className="px-4 py-2.5 text-center font-semibold">On Hand</th>
                  <th className="px-4 py-2.5 text-center font-semibold">Reorder Qty</th>
                  <th className="px-4 py-2.5 text-center font-semibold">Status</th>
                </tr>
              </thead>
              <tbody>
                {alertItems.map((item, idx) => (
                  <tr
                    key={item.stockNo}
                    className={`border-b border-slate-50 last:border-0 ${
                      idx % 2 === 0 ? "bg-white" : "bg-slate-50/50"
                    }`}
                  >
                    <td className="px-4 py-2.5 font-mono text-xs font-semibold text-slate-700">
                      {item.stockNo}
                    </td>
                    <td className="px-4 py-2.5 text-slate-700 text-xs max-w-[240px] truncate">
                      {item.description}
                    </td>
                    <td className="px-4 py-2.5 text-slate-500 text-xs">{item.unit}</td>
                    <td className="px-4 py-2.5 text-center text-xs font-semibold text-slate-800">
                      {item.onHand}
                    </td>
                    <td className="px-4 py-2.5 text-center text-xs text-slate-500">
                      {item.reorderQty}
                    </td>
                    <td className="px-4 py-2.5 text-center">
                      {item.isOutOfStock ? (
                        <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold bg-danger-100 text-danger-500 border border-red-200">
                          Out of Stock
                        </span>
                      ) : (
                        <span className="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-semibold bg-amber-100 text-amber-500 border border-amber-200">
                          Low Stock
                        </span>
                      )}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

    </div>
  );
}
